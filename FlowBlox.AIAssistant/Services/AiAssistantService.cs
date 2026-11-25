using FlowBlox.Core.Constants;
using System.Text;
using FlowBlox.Core;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Constants;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Services;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Compression;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Json;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Services
{
    public partial class AiAssistantService
    {
        private const int AutomaticAdjustmentDelayMilliseconds = 500;
        internal const string ContinuationModelPrompt =
            "Continue from the latest stored Tool API response in the conversation history. " +
            "Return exactly one assistant JSON object using the required schema. " +
            "Do not write prose outside the JSON object.";

        private readonly IAiExecutor _executor;
        private readonly IModelPricingCatalogueService _pricingCatalogue;
        private readonly IFlowBloxAIToolApi _tools;
        private readonly IAiResponseInstructionParserService _instructionParser;
        private readonly AssistantOutputFormatFeedbackQueue _outputFormatFeedbackQueue;
        private readonly ILogger? _logger;
        private readonly Func<AssistantConfiguration>? _configurationProvider;
        private readonly object _sessionSync = new();
        private int _activeRunCount;
        private ModelPricing? _currentModelPricing;
        private AssistantSessionState? _session;

        public AssistantComponentContext? ComponentContext { get; set; }

        public event EventHandler<AssistantTranscriptLine>? TranscriptLineAdded;
        public event EventHandler? SessionUsageChanged;
        public event EventHandler<AssistantCommunicationStatusChangedEventArgs>? CommunicationStatusChanged;
        public event EventHandler? DebugStateChanged;
        public event EventHandler? ConversationSummaryChanged;
        public event EventHandler? BeforeAutomaticLayoutAdjustment;

        public AiAssistantService(
            IAiExecutor executor,
            IFlowBloxAIToolApi tools,
            ILogger? logger = null,
            Func<AssistantConfiguration>? configurationProvider = null,
            IModelPricingCatalogueService? pricingCatalogue = null)
        {
            _pricingCatalogue = pricingCatalogue ?? FlowBloxServiceLocator.Instance.GetService<IModelPricingCatalogueService>();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _instructionParser = FlowBloxServiceLocator.Instance.GetService<IAiResponseInstructionParserService>();
            _outputFormatFeedbackQueue = new AssistantOutputFormatFeedbackQueue();
            _logger = logger;
            _configurationProvider = configurationProvider;
        }

        public void ResetSession()
        {
            lock (_sessionSync)
            {
                if (_session != null)
                    ToolHandlerUtilities.ClearSessionCache(_session.SessionId);

                _session = null;
            }

            CurrentContextTokens = 0;
            _currentModelPricing = null;
            var budget = BuildTokenBudget(GetConfiguration(out _));
            EffectiveContextLimit = Math.Max(0, budget.MaxContextTokens - budget.ReservedResponseTokens);
            RaiseSessionUsageChanged();
            ConversationSummaryChanged?.Invoke(this, EventArgs.Empty);
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }


        public void RestoreSession(AiAssistantHistoryDocument history)
        {
            _currentModelPricing = null;
            CurrentContextTokens = Math.Max(0, history?.CurrentContextTokens ?? 0);
            var configuration = GetConfiguration(out _);
            var budget = BuildTokenBudget(configuration);
            EffectiveContextLimit = history?.EffectiveContextLimit ?? Math.Max(0, budget.MaxContextTokens - budget.ReservedResponseTokens);
            lock (_sessionSync)
            {
                if (_session != null)
                    ToolHandlerUtilities.ClearSessionCache(_session.SessionId);

                var session = new AssistantSessionState
                {
                    ConversationSummary = history?.ConversationSummary ?? string.Empty,
                    SummarizedMessageCount = Math.Max(0, history?.SummarizedMessageCount ?? 0),
                    LastContextJsonHash = history?.LastContextJsonHash ?? string.Empty,
                    Usage = history?.Usage?.Clone() ?? new AssistantSessionUsage()
                };

                var sessionMessages = history?.SessionMessages?
                    .Where(x => !string.IsNullOrWhiteSpace(x?.CompleteMessage))
                    .Select(x => x.Clone())
                    .ToList() ?? new List<AssistantSessionMessage>();

                session.Messages.AddRange(sessionMessages);
                session.RetainedContents.AddRange(history?.RetainedContents?.Select(content => content.Clone()) ?? []);
                session.SummarizedMessageCount = Math.Clamp(session.SummarizedMessageCount, 0, session.Messages.Count);
                session.RuntimeBindingsReset = session.Messages.Count > 0 || !string.IsNullOrWhiteSpace(session.ConversationSummary);
                _session = session;
                RaiseSessionUsageChanged();
            }

            ConversationSummaryChanged?.Invoke(this, EventArgs.Empty);
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateHistorySessionMetadata(AiAssistantHistoryDocument history)
        {
            if (history == null)
                return;

            lock (_sessionSync)
            {
                var session = _session ?? new AssistantSessionState();
                history.LastContextJsonHash = session.LastContextJsonHash;
                history.ConversationSummary = session.ConversationSummary;
                history.SummarizedMessageCount = session.SummarizedMessageCount;
                history.Usage = session.Usage.Clone();
                history.CurrentContextTokens = CurrentContextTokens;
                history.EffectiveContextLimit = EffectiveContextLimit;
                history.LoadedExplanationKeys = []; // Legacy keys do not contain verbatim explanation payloads.
                history.RetainedContents = session.RetainedContents.Select(content => content.Clone()).ToList();
                history.SessionMessages = session.Messages
                    .Where(x => !string.IsNullOrWhiteSpace(x?.CompleteMessage))
                    .Select(x => x.Clone())
                    .ToList();
            }
        }

        public AssistantSessionUsage SessionUsage
        {
            get
            {
                lock (_sessionSync)
                    return (_session?.Usage ?? new AssistantSessionUsage()).Clone();
            }
        }

        public int CurrentContextTokens { get; private set; }
        public int EffectiveContextLimit { get; private set; } = AssistantConfigurationLimits.DefaultMaxContextTokens - 4096;

        public string CurrentConversationSummary
        {
            get
            {
                lock (_sessionSync)
                    return _session?.ConversationSummary ?? string.Empty;
            }
        }

        public void ResetSessionUsage()
        {
            lock (_sessionSync)
                GetOrCreateSession().Usage = new AssistantSessionUsage();

            RaiseSessionUsageChanged();
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public AssistantConfiguration GetConfiguration(out string error)
        {
            error = string.Empty;
            if (_configurationProvider != null)
                return _configurationProvider() ?? throw new InvalidOperationException("Assistant configuration provider returned null.");

            var rawConfig = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantConfiguration)?.Value ?? string.Empty;
            var parseResult = AssistantConfigurationJson.Parse(rawConfig);
            if (parseResult.HasError)
            {
                error = parseResult.Error;
                return new AssistantConfiguration();
            }

            return parseResult.Configuration ?? new AssistantConfiguration();
        }

        public bool SaveConfiguration(AssistantConfiguration configuration, out string error)
        {
            error = string.Empty;
            try
            {
                ArgumentNullException.ThrowIfNull(configuration);

                var serialized = AssistantConfigurationJson.Serialize(configuration);
                var options = FlowBloxOptions.GetOptionInstance();
                var option = options.GetOption(OptionsConstants.AIAssistantConfiguration);
                if (option == null)
                {
                    error = $"Option 'AI.AssistantConfiguration' not found.";
                    return false;
                }

                option.Value = serialized;
                options.Save();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private AssistantTokenBudget BuildTokenBudget(AssistantConfiguration configuration)
        {
            return AssistantTokenBudget.Create(configuration, _currentModelPricing, _session?.TokenEstimateMultiplier ?? 1d);
        }

        public async Task<AssistantResult> ExecutePromptAsync(
            string userPrompt,
            CancellationToken ct,
            AssistantRunControl? runControl = null)
        {
            var result = new AssistantResult();

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                result.Success = false;
                var message = "Prompt is empty.";
                result.Errors.Add(message);
                AddTranscript(result, AssistantTranscriptKind.Error, message);
                return result;
            }

            var config = GetConfiguration(out var configurationError);
            if (!string.IsNullOrWhiteSpace(configurationError))
            {
                result.Success = false;
                result.Errors.Add(configurationError);
                AddTranscript(result, AssistantTranscriptKind.Error, configurationError);
                return result;
            }

            ComponentContext?.EnsureActive();
            using var componentScope = ToolHandlerUtilities.BeginComponentScope(ComponentContext);
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            using var registryScope = project == null
                ? null
                : FlowBloxRegistryProvider.BeginScopedRegistry(ComponentContext?.Registry ?? project.FlowBloxRegistry);

            var maxToolRounds = Math.Clamp(
                config.MaxToolRounds,
                AssistantConfigurationLimits.MinToolRounds,
                AssistantConfigurationLimits.MaxToolRounds);
            var maxLatestMessages = Math.Clamp(
                config.MaxLatestMessages,
                AssistantConfigurationLimits.MinLatestMessages,
                AssistantConfigurationLimits.MaxLatestMessages);
            var minLatestMessages = Math.Clamp(
                config.MinLatestMessages,
                AssistantConfigurationLimits.MinLatestMessages,
                maxLatestMessages);
            var summaryCompactionRate = Math.Clamp(
                config.SummaryCompactionRate,
                AssistantConfigurationLimits.MinSummaryCompactionRate,
                AssistantConfigurationLimits.MaxSummaryCompactionRate);
            var selectedProvider = config.SelectedProvider;
            var modelPricing = selectedProvider == null ? null : await _pricingCatalogue.GetPricingAsync(
                selectedProvider.ProviderType,
                selectedProvider.DefaultModel,
                ct).ConfigureAwait(false);
            _currentModelPricing = modelPricing;
            var tokenBudget = BuildTokenBudget(config);
            var session = GetOrCreateSession();
            lock (_sessionSync)
                session.LastSummaryCompactionAttemptKey = string.Empty;
            _outputFormatFeedbackQueue.ClearIncorrectOutputFormatData();
            ToolHandlerUtilities.SetCurrentSessionGuid(session.SessionId);
            var currentContextJson = ComponentContext?.CaptureJson() ?? GetCurrentProjectJson();
            var currentContextJsonHash = ComputeJsonHash(currentContextJson);
            var hasKnownContextJsonHash = !string.IsNullOrWhiteSpace(session.LastContextJsonHash);
            var contextJsonChanged = !string.Equals(session.LastContextJsonHash, currentContextJsonHash, StringComparison.OrdinalIgnoreCase);
            var shouldAttachContextJson = config.AttachProjectJsonAutomatically && contextJsonChanged;
            var attachmentInformation = ResolveSnapshotAttachmentInformation(
                config.AttachProjectJsonAutomatically,
                hasKnownContextJsonHash,
                contextJsonChanged);
            var toolDefinitions = _tools.GetToolDefinitions();
            var systemPrompt = AssistantPromptBuilder.BuildSystemPrompt();
            var sessionBootstrapPrompt = AssistantPromptBuilder.BuildSessionBootstrapPrompt(toolDefinitions, session.RuntimeBindingsReset);
            lock (_sessionSync)
                session.InitialSystemMessages = CreateInitialSystemMessages(systemPrompt, sessionBootstrapPrompt);
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
            var protocolWriter = TryCreateCommunicationProtocolWriter(config, userPrompt, session.SessionId);
            var formatRetryIssued = false;
            var hasExecutedLayoutRelevantToolCall = false;
            protocolWriter?.AppendAiAssistantServiceText("System prompt prepared", systemPrompt);
            protocolWriter?.AppendAiAssistantServiceText("Session bootstrap prompt prepared", sessionBootstrapPrompt);

            try
            {
                Interlocked.Increment(ref _activeRunCount);
                var summaryTargetMessageCount = session.SummarizedMessageCount;

                for (var toolRound = 1; toolRound <= maxToolRounds; toolRound++)
                {
                    tokenBudget = BuildTokenBudget(config);
                    ct.ThrowIfCancellationRequested();
                    if (runControl != null)
                        await PrepareRunControlForNextRoundAsync(
                            session,
                            runControl,
                            hasExecutedLayoutRelevantToolCall,
                            ct).ConfigureAwait(false);
                    var initialUserPrompt = toolRound == 1
                        ? AssistantPromptBuilder.BuildInitialUserPrompt(
                            userPrompt,
                            shouldAttachContextJson ? currentContextJson : null,
                            attachmentInformation,
                            ToolHandlerUtilities.GetActiveSkillProjectName(),
                            ComponentContext)
                        : string.Empty;
                    var modelPrompt = toolRound == 1
                        ? initialUserPrompt
                        : ContinuationModelPrompt;

                    var outputFormatFeedback = _outputFormatFeedbackQueue.DequeuePromptOrEmpty();
                    if (!string.IsNullOrWhiteSpace(outputFormatFeedback))
                    {
                        modelPrompt = string.IsNullOrWhiteSpace(modelPrompt)
                            ? outputFormatFeedback
                            : modelPrompt.TrimEnd() + Environment.NewLine + outputFormatFeedback;
                        protocolWriter?.AppendAiAssistantServiceText("Output format feedback attached", outputFormatFeedback);
                    }
                    protocolWriter?.AppendAiAssistantServiceText($"Round {toolRound} prompt prepared", modelPrompt);

                    var chatRequestResult = AssistantChatRequestBuilder.Build(
                        systemPrompt,
                        sessionBootstrapPrompt,
                        session.ConversationSummary,
                        session.Messages,
                        modelPrompt,
                        maxLatestMessages,
                        minLatestMessages,
                        config.SelectedProvider,
                        tokenBudget,
                        session.SummarizedMessageCount,
                        GetRuntimeSystemContents(session),
                        0);
                    summaryTargetMessageCount = Math.Max(
                        summaryTargetMessageCount,
                        CalculateSummaryCompactionTarget(
                            session,
                            chatRequestResult.FirstIncludedHistoryMessageIndex,
                            minLatestMessages,
                            summaryCompactionRate));
                    summaryTargetMessageCount = await TryUpdateConversationSummaryAsync(
                        session,
                        summaryTargetMessageCount,
                        config,
                        userPrompt,
                        ct,
                        modelPrompt).ConfigureAwait(false);

                    // Summarization can take long enough for the user to add further instructions.
                    if (runControl != null)
                        await PrepareRunControlForNextRoundAsync(
                            session,
                            runControl,
                            hasExecutedLayoutRelevantToolCall,
                            ct).ConfigureAwait(false);

                    chatRequestResult = AssistantChatRequestBuilder.Build(
                        systemPrompt,
                        sessionBootstrapPrompt,
                        session.ConversationSummary,
                        session.Messages,
                        modelPrompt,
                        maxLatestMessages,
                        minLatestMessages,
                        config.SelectedProvider,
                        tokenBudget,
                        session.SummarizedMessageCount,
                        GetRuntimeSystemContents(session),
                        0);
                    var chatRequest = chatRequestResult.Request;
                    if (toolRound == 1)
                        AppendSingleSessionMessage(session, "user", initialUserPrompt);

                    var exec = await ExecuteChatWithCommunicationStatusAsync(
                        chatRequest,
                        config,
                        "Thinking...",
                        ct).ConfigureAwait(false);
                    result.RawModelOutput = exec.RawOutput ?? exec.OutputText ?? string.Empty;

                    if (!exec.Success)
                    {
                        result.Success = false;
                        var error = string.IsNullOrWhiteSpace(exec.Error) ? "AI request failed." : exec.Error;
                        result.Errors.Add(error);
                        AddTranscript(result, AssistantTranscriptKind.Error, error);
                        _logger?.Warn($"AI Assistant execution failed: {error}");
                        return result;
                    }

                    var assistantOutput = exec.OutputText ?? string.Empty;

                    var parseResult = _instructionParser.Parse(assistantOutput, config.SelectedProvider);
                    if (!parseResult.Success || parseResult.Instruction == null)
                    {
                        AppendSingleSessionMessage(session, "assistant", assistantOutput);
                        protocolWriter?.AppendAiText(toolRound, assistantOutput);
                        AddTranscript(
                            result,
                            AssistantTranscriptKind.Assistant,
                            assistantOutput,
                            assistantOutput);

                        if (!formatRetryIssued)
                        {
                            formatRetryIssued = true;
                            var formatGuidance = _outputFormatFeedbackQueue.Enqueue(parseResult);

                            AddTranscript(result, AssistantTranscriptKind.Status,
                                "Response format invalid. Retrying once.");
                            result.Warnings.Add("Assistant response format invalid; retrying once with explicit format guidance.");
                            protocolWriter?.AppendAiAssistantServiceText("Format validation guidance issued", formatGuidance);

                            continue;
                        }

                        result.Success = false;
                        result.Errors.Add("Assistant returned an invalid response format twice. Aborting execution.");
                        AddTranscript(result, AssistantTranscriptKind.Error,
                            "Invalid response format repeated after correction. Aborting.");
                        UpdateKnownContextJsonHash(session);
                        summaryTargetMessageCount = await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, userPrompt, ct).ConfigureAwait(false);
                        return result;
                    }

                    formatRetryIssued = false;

                    _outputFormatFeedbackQueue.ClearIncorrectOutputFormatData();
                    var instruction = parseResult.Instruction;
                    EnsureAssistantActivityMessage(instruction, parseResult.JsonObject);
                    var assistantInstructionContent = string.IsNullOrWhiteSpace(instruction.InternalContent)
                        ? assistantOutput
                        : instruction.InternalContent;

                    protocolWriter?.AppendAiJson(toolRound, parseResult.JsonObject);

                    var assistantRoundMessage = BuildAssistantRoundMessage(instruction, assistantOutput);
                    AddTranscript(
                        result,
                        AssistantTranscriptKind.Assistant,
                        assistantRoundMessage,
                        instruction.InternalContent);

                    if (instruction.ToolCalls.Count == 0 || instruction.Final)
                    {
                        var finalText = string.IsNullOrWhiteSpace(instruction.AssistantMessage)
                            ? assistantOutput
                            : instruction.AssistantMessage;
                        AppendSingleSessionMessage(session, "assistant", finalText);

                        if (hasExecutedLayoutRelevantToolCall)
                            await DelayAndRunAutomaticAdjustmentIfEnabledAsync("AI run completed", ct).ConfigureAwait(false);

                        result.Success = true;
                        result.AssistantText = instruction.AssistantMessage;
                        result.Summary = "Assistant response generated.";
                        UpdateKnownContextJsonHash(session);
                        summaryTargetMessageCount = await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, userPrompt, ct).ConfigureAwait(false);

                        // Keep accepting messages until all asynchronous completion work is finished.
                        if (runControl != null && !runControl.TryCompleteWhenNoMessagesAreStaged())
                            continue;

                        return result;
                    }

                    var roundToolTranscript = new List<string>();
                    var executedToolCalls = new List<ExecutedToolCallInfo>();

                    var processingStatus = BuildToolProcessingTranscript(instruction.ToolCalls);
                    AddTranscript(
                        result,
                        AssistantTranscriptKind.ToolProcessing,
                        "Processing requested operations...",
                        processingStatus);

                    var assistantToolRequestPersisted = false;
                    try
                    {
                        using var retainedContentScope = ToolHandlerUtilities.BeginRetainedContentScope(key =>
                        {
                            lock (_sessionSync)
                                return AssistantHistoryRetention.FindLocation(
                                    session.Messages.Skip(chatRequestResult.FirstIncludedHistoryMessageIndex).ToArray(),
                                    session.RetainedContents,
                                    key);
                        }, requireToolDefinitions: true);

                        for (var toolCallIndex = 0; toolCallIndex < instruction.ToolCalls.Count; toolCallIndex++)
                        {
                            var toolCall = instruction.ToolCalls[toolCallIndex];
                            ct.ThrowIfCancellationRequested();

                            var request = new ToolRequest
                            {
                                ToolName = toolCall.ToolName,
                                Arguments = toolCall.Arguments,
                                CorrelationId = Guid.NewGuid().ToString("N")
                            };

                            AddTranscript(
                                result,
                                AssistantTranscriptKind.ToolProcessing,
                                $"Processing tool request {toolCallIndex + 1}/{instruction.ToolCalls.Count}: {request.ToolName}",
                                (request.Arguments ?? new JObject()).ToString(Formatting.Indented));

                            var response = await _tools.ExecuteAsync(request, ct).ConfigureAwait(false);
                            DebugStateChanged?.Invoke(this, EventArgs.Empty);
                            if (response.IsLayoutRelevantForAutoAdjustment)
                                hasExecutedLayoutRelevantToolCall = true;

                            executedToolCalls.Add(new ExecutedToolCallInfo
                            {
                                ToolName = request.ToolName,
                                Arguments = request.Arguments,
                                Response = response
                            });

                            if (!response.Ok)
                            {
                                result.Warnings.Add($"Tool '{request.ToolName}' reported a problem: {response.Error}");
                                _logger?.Warn($"Assistant tool call reported a problem. Tool={request.ToolName}, Error={response.Error}");
                            }

                        }

                        // Refresh response metadata without executing handlers again.
                        foreach (var executed in executedToolCalls)
                        {
                            executed.Response.RefreshForSerialization();
                            var completedRequest = new ToolRequest
                            {
                                ToolName = executed.ToolName,
                                Arguments = executed.Arguments
                            };
                            protocolWriter?.AppendToolCall(
                                toolRound,
                                completedRequest,
                                executed.Response);
                            roundToolTranscript.Add(SerializeToolTranscript(completedRequest, executed.Response));
                        }
                        var toolApiResponse = AssistantPromptBuilder.BuildToolApiResponsePrompt(
                            roundToolTranscript,
                            ToolHandlerUtilities.GetActiveSkillProjectName());
                        AppendMessagePair(
                            session,
                            assistantInstructionContent,
                            toolApiResponse,
                            exec.MessageMetadata,
                            executedToolCalls.SelectMany((call, resultIndex) => call.Response.RetainedContents.Select(content =>
                            {
                                var retained = content.Clone();
                                retained.ResultIndex = resultIndex;
                                return retained;
                            })));
                        assistantToolRequestPersisted = true;
                    }
                    finally
                    {
                        if (!assistantToolRequestPersisted)
                            AppendSingleSessionMessage(session, "assistant", assistantInstructionContent);
                    }

                    var executionSummary = BuildToolExecutionSummary(executedToolCalls);

                    var internalStatus = BuildToolExecutionTranscript(
                        executionSummary.RequestedOperationCount,
                        executionSummary.SuccessfulOperationCount,
                        executionSummary.FailedToolNames,
                        executedToolCalls);

                    var toolExecutionKind = executionSummary.AllSuccessful
                        ? AssistantTranscriptKind.ToolSuccess
                        : executionSummary.AnySuccessful
                            ? AssistantTranscriptKind.ToolPartialSuccess
                            : AssistantTranscriptKind.ToolError;

                    var toolExecutionMessage = BuildToolExecutionMessage(executionSummary);
                    AddTranscript(
                        result,
                        toolExecutionKind,
                        toolExecutionMessage,
                        internalStatus);

                    ct.ThrowIfCancellationRequested();
                }

                result.Success = false;
                result.Errors.Add($"Assistant reached max tool rounds ({maxToolRounds}) without a final response.");
                AddTranscript(result, AssistantTranscriptKind.Error,
                    $"Reached max tool rounds ({maxToolRounds}) without a final response.");
                UpdateKnownContextJsonHash(session);
                summaryTargetMessageCount = await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, userPrompt, ct).ConfigureAwait(false);
                return result;
            }
            finally
            {
                CompleteRunControl(session, runControl);
                ToolHandlerUtilities.SetCurrentSessionGuid(null);
                DebugStateChanged?.Invoke(this, EventArgs.Empty);
                Interlocked.Decrement(ref _activeRunCount);
                protocolWriter?.TryWrite(_logger);
            }
        }

        private void AppendStagedUserMessages(
            AssistantSessionState session,
            AssistantRunControl runControl)
        {
            foreach (var message in runControl.DrainStagedUserMessages())
                AppendSingleSessionMessage(session, "user", message);
        }

        private void CompleteRunControl(
            AssistantSessionState session,
            AssistantRunControl? runControl)
        {
            if (runControl == null)
                return;

            runControl.Complete();
            AppendStagedUserMessages(session, runControl);
        }

        private async Task PrepareRunControlForNextRoundAsync(
            AssistantSessionState session,
            AssistantRunControl runControl,
            bool hasExecutedLayoutRelevantToolCall,
            CancellationToken ct)
        {
            if (runControl.IsPauseRequested && hasExecutedLayoutRelevantToolCall)
                await DelayAndRunAutomaticAdjustmentIfEnabledAsync("AI run paused", ct).ConfigureAwait(false);

            await runControl.WaitWhilePausedAsync(ct).ConfigureAwait(false);
            AppendStagedUserMessages(session, runControl);
        }

        private async Task DelayAndRunAutomaticAdjustmentIfEnabledAsync(string reason, CancellationToken ct)
        {
            var configuration = GetConfiguration(out _);
            if (!configuration.EnableAutomaticAdjustment)
                return;

            await Task.Delay(AutomaticAdjustmentDelayMilliseconds, ct).ConfigureAwait(false);
            RunAutomaticAdjustment(reason);
        }

        private void RunAutomaticAdjustment(string reason)
        {
            OnBeforeAutomaticLayoutAdjustment();
            var layoutResult = FlowBlockAutoLayoutAdjuster.AdjustCurrentRegistryLayout();
            _logger?.Info(
                $"AutoAdjustFlowLayout executed ({reason}). Updated={layoutResult.UpdatedFlowBlocks}, Total={layoutResult.TotalFlowBlocks}, Components={layoutResult.ComponentsProcessed}");
        }

        private void OnBeforeAutomaticLayoutAdjustment()
        {
            try
            {
                BeforeAutomaticLayoutAdjustment?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Before auto layout adjustment handler failed: {ex.Message}", ex);
            }
        }

        private AiCommunicationProtocolWriter? TryCreateCommunicationProtocolWriter(AssistantConfiguration config, string userPrompt, string sessionId)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!config.EnableCommunicationProtocol)
                return null;

            try
            {
                var options = FlowBloxOptions.GetOptionInstance();
                var directory = options
                    .GetOption(OptionsConstants.AICommuncationProtocolDir)?
                    .Value;

                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FlowBlox",
                        "logs",
                        "ai_assistant_protocol");
                }

                return new AiCommunicationProtocolWriter(directory, sessionId, userPrompt);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Could not initialize communication protocol writer: {ex.Message}");
                return null;
            }
        }

        private AssistantSessionState GetOrCreateSession()
        {
            lock (_sessionSync)
            {
                _session ??= new AssistantSessionState();
                return _session;
            }
        }

        private static IReadOnlyList<AssistantSystemMessage> GetRuntimeSystemMessages(AssistantSessionState session) =>
            AssistantHistoryRetention.GetSystemMessages(session.RetainedContents);

        private static IReadOnlyList<string> GetRuntimeSystemContents(AssistantSessionState session) =>
            GetRuntimeSystemMessages(session).Select(message => message.Content).ToArray();

        private static IReadOnlyList<AssistantSystemMessage> CreateInitialSystemMessages(
            string systemPrompt,
            string bootstrap) =>
            [
                new(
                    "system",
                    "System Prompt",
                    AssistantSystemMessageKind.SystemPrompt,
                    systemPrompt),
                new(
                    "bootstrap",
                    "Session Bootstrap",
                    AssistantSystemMessageKind.SessionBootstrap,
                    bootstrap)
            ];

        public AssistantDebugSnapshot GetDebugSnapshot()
        {
            using var componentScope = ToolHandlerUtilities.BeginComponentScope(ComponentContext);
            var configuration = GetConfiguration(out _);
            lock (_sessionSync)
            {
                var session = GetOrCreateSession();
                session.InitialSystemMessages ??= CreateInitialSystemMessages(
                    AssistantPromptBuilder.BuildSystemPrompt(),
                    AssistantPromptBuilder.BuildSessionBootstrapPrompt(_tools.GetToolDefinitions(), session.RuntimeBindingsReset));
                var systemMessages = session.InitialSystemMessages
                    .Concat(GetRuntimeSystemMessages(session))
                    .ToArray();
                return new AssistantDebugSnapshot(
                    session.ConversationSummary,
                    session.Messages.Select((
                        message,
                        index) => new AssistantDebugHistoryMessage(
                        message.Clone(),
                        index < session.SummarizedMessageCount))
                        .ToArray(),
                    systemMessages,
                    BuildTokenBudget(configuration).ApproximateCharactersPerToken);
            }
        }

        private void AppendSingleSessionMessage(AssistantSessionState session, string role, string content)
        {
            if (session == null || string.IsNullOrWhiteSpace(content))
                return;

            lock (_sessionSync)
            {
                session.Messages.Add(new AssistantSingleMessage
                {
                    MessageRole = string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant"
                        : "user",
                    Message = content.Trim()
                });
            }
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void AppendMessagePair(
            AssistantSessionState session,
            string assistantRequest,
            string toolApiResponse,
            AIChatMessageMetadata? metadata,
            IEnumerable<AssistantRetainedContent> retainedContent)
        {
            if (session == null ||
                string.IsNullOrWhiteSpace(assistantRequest) ||
                string.IsNullOrWhiteSpace(toolApiResponse))
                return;

            lock (_sessionSync)
            {
                session.Messages.Add(new AssistantMessagePair
                {
                    AssistantRequest = assistantRequest.Trim(),
                    ToolApiResponse = toolApiResponse.Trim(),
                    Metadata = metadata?.Clone() ?? new AIChatMessageMetadata(),
                    RetainedContents = retainedContent
                        .Select(content => content.Clone())
                        .ToList()
                });
            }
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task<int> TryUpdateConversationSummaryAsync(
            AssistantSessionState session,
            int targetSummarizedMessageCount,
            AssistantConfiguration config,
            string currentTask,
            CancellationToken ct,
            string? modelPrompt = null)
        {
            string originalSummary;
            int originalCount;
            AIChatRequest? summaryRequest = null;
            lock (_sessionSync)
            {
                originalSummary = session.ConversationSummary;
                originalCount = session.SummarizedMessageCount;
                targetSummarizedMessageCount = Math.Clamp(targetSummarizedMessageCount, originalCount, session.Messages.Count);
                if (targetSummarizedMessageCount > originalCount)
                {
                    var retained = session.RetainedContents.Select(content => content.Clone()).ToList();
                    retained.AddRange(AssistantHistoryRetention.GetPendingPromotions(session.Messages, session.RetainedContents));
                    var messages = session.Messages
                        .Skip(originalCount)
                        .Take(targetSummarizedMessageCount - originalCount)
                        .ToArray();
                    var initial = session.InitialSystemMessages!;
                    var prefix = AssistantChatRequestBuilder.BuildMaintenancePrefix(
                        initial[0].Content,
                        initial[1].Content,
                        originalSummary,
                        session.Messages,
                        originalCount,
                        targetSummarizedMessageCount,
                        config.SelectedProvider,
                        GetRuntimeSystemContents(session));
                    summaryRequest = AssistantSummaryRequestBuilder.Build(originalSummary, messages, retained, prefix);
                    summaryRequest = ChooseMaintenanceRequest(
                        summaryRequest,
                        AssistantSummaryRequestBuilder.Build(originalSummary, messages, retained),
                        prefix,
                        config);
                }
            }

            var mergedSummary = summaryRequest == null
                ? null
                : await TryMergeConversationSummaryAsync(summaryRequest, config, ct).ConfigureAwait(false);
            var merged = mergedSummary != null;
            var branch = merged && summaryRequest!.SystemMessages.Count > 1
                ? AssistantMaintenanceRequestBuilder.Continue(summaryRequest, mergedSummary!)
                : null;
            var summary = await TryCompactConversationSummaryAsync(
                session,
                mergedSummary ?? originalSummary,
                config,
                currentTask,
                ct,
                branch).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var transitioned = merged || summary != originalSummary;
            if (transitioned)
            {
                lock (_sessionSync)
                {
                    // Publish one context transition only after all summary work completed successfully.
                    if (session.ConversationSummary != originalSummary || session.SummarizedMessageCount != originalCount)
                        return session.SummarizedMessageCount;
                    session.ConversationSummary = summary;
                    if (merged)
                        session.SummarizedMessageCount = targetSummarizedMessageCount;
                    AssistantHistoryRetention.Promote(session.Messages, session.RetainedContents);
                }
                ConversationSummaryChanged?.Invoke(this, EventArgs.Empty);
                DebugStateChanged?.Invoke(this, EventArgs.Empty);
            }
            if (merged)
                await TryCompactLatestToolResultsAsync(session, config, currentTask, modelPrompt ?? currentTask, ct).ConfigureAwait(false);
            return targetSummarizedMessageCount;
        }

        private bool FitsMaintenanceBudget(AIChatRequest request, AssistantConfiguration config)
        {
            var budget = BuildTokenBudget(config);
            var tokens = EstimateInputTokens(request, budget);
            return tokens + Math.Max(config.ReservedResponseTokens, config.MaxTokens ?? 0) <= budget.MaxContextTokens;
        }

        private AIChatRequest ChooseMaintenanceRequest(
            AIChatRequest shared,
            AIChatRequest targeted,
            AIChatRequest prefix,
            AssistantConfiguration config)
        {
            if (!FitsMaintenanceBudget(shared, config))
                return targeted;

            var budget = BuildTokenBudget(config);
            var sharedTokens = EstimateInputTokens(shared, budget);
            var targetedTokens = EstimateInputTokens(targeted, budget);
            var cachedTokens = EstimateInputTokens(prefix, budget);
            // The just-generated response may not yet be cached by the provider.
            if (prefix.Messages.LastOrDefault()?.Role == "assistant")
                cachedTokens -= budget.EstimateTokens(prefix.Messages[^1].Content);
            var sharedCost = AssistantCostCalculator.Calculate(new AIUsage
            {
                TotalInputTokens = sharedTokens,
                CachedInputTokens = cachedTokens,
                UncachedInputTokens = sharedTokens - cachedTokens,
                CacheWriteInputTokens = 0,
                OutputTokens = 0
            }, _currentModelPricing);
            var targetedCost = AssistantCostCalculator.Calculate(new AIUsage
            {
                TotalInputTokens = targetedTokens,
                CachedInputTokens = 0,
                UncachedInputTokens = targetedTokens,
                CacheWriteInputTokens = 0,
                OutputTokens = 0
            }, _currentModelPricing);
            // Do not replay a large prefix if even its optimistic cache-read cost exceeds the short request.
            return sharedCost.HasValue && targetedCost.HasValue && sharedCost.Value.Cost >= targetedCost.Value.Cost
                ? targeted
                : shared;
        }

        private static long EstimateInputTokens(AIChatRequest request, AssistantTokenBudget budget) =>
            request.SystemMessages.Concat(request.Messages).Sum(message => (long)budget.EstimateTokens(message.Content));

        private async Task<string?> TryMergeConversationSummaryAsync(
            AIChatRequest request,
            AssistantConfiguration config,
            CancellationToken ct)
        {
            try
            {
                var result = await ExecuteChatWithCommunicationStatusAsync(request, config, "Summarize...", ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (result.Success && !string.IsNullOrWhiteSpace(result.OutputText))
                    return result.OutputText.Trim();
                if (!string.IsNullOrWhiteSpace(result.Error))
                    _logger?.Warn($"AI Assistant summary update failed: {result.Error}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"AI Assistant summary update failed: {ex.Message}");
            }
            return null;
        }

        private async Task<string> TryCompactConversationSummaryAsync(
            AssistantSessionState session,
            string summary,
            AssistantConfiguration config,
            string currentTask,
            CancellationToken ct,
            AIChatRequest? prefix)
        {
            var budget = BuildTokenBudget(config);
            var limit = Math.Max(1, config.MaxSummaryTokens);
            var target = Math.Max(1, (int)Math.Floor(limit * Math.Clamp(config.SummaryCompactionTargetRatio, 0.1d, 0.9d)));
            if (budget.EstimateTokens(summary) <= limit)
                return summary;
            var attemptKey = BuildSummaryCompactionAttemptKey(summary, currentTask, limit, target);
            lock (_sessionSync)
            {
                if (attemptKey == session.LastSummaryCompactionAttemptKey)
                    return summary;
            }
            var compacted = await TryCompactContentAsync(
                summary,
                config,
                currentTask,
                limit,
                target,
                ct,
                prefix: prefix).ConfigureAwait(false);
            lock (_sessionSync)
                session.LastSummaryCompactionAttemptKey = BuildSummaryCompactionAttemptKey(compacted, currentTask, limit, target);
            if (budget.EstimateTokens(compacted) > target)
                _logger?.Warn($"AI Assistant context compaction did not reach its target of {target} estimated tokens.");
            return compacted;
        }

        private static string BuildSummaryCompactionAttemptKey(
            string summary,
            string currentTask,
            int maxSummaryTokens,
            int targetTokens)
        {
            return HashHelper.ComputeSHA256Hash(Encoding.UTF8.GetBytes(
                FormattableString.Invariant($"{maxSummaryTokens}:{targetTokens}:{currentTask.Length}:{currentTask}{summary}")));
        }

        private int CalculateSummaryCompactionTarget(
            AssistantSessionState session,
            int firstIncludedHistoryMessageIndex,
            int latestMessagesToKeep,
            double compactionRate)
        {
            if (session == null)
                return 0;

            lock (_sessionSync)
            {
                if (firstIncludedHistoryMessageIndex <= session.SummarizedMessageCount)
                    return session.SummarizedMessageCount;

                var clampedLatestMessagesToKeep = Math.Clamp(latestMessagesToKeep, 0, session.Messages.Count);
                var summarizableMessageCount = Math.Max(
                    0,
                    session.Messages.Count - clampedLatestMessagesToKeep - session.SummarizedMessageCount);
                var rateCompactionTarget = session.SummarizedMessageCount +
                                           (int)Math.Ceiling(summarizableMessageCount * compactionRate);

                return Math.Max(
                    firstIncludedHistoryMessageIndex,
                    rateCompactionTarget);
            }
        }

        private static string ComputeJsonHash(string? json)
        {
            return HashHelper.ComputeSHA256Hash(Encoding.UTF8.GetBytes(json ?? string.Empty));
        }

        private static int EstimateRequestTokens(AIChatRequest request, AssistantTokenBudget budget)
        {
            return request.SystemMessages.Concat(request.Messages)
                .Sum(message => budget.EstimateTokens(message.Content) + 4) + 3;
        }

        private void RaiseSessionUsageChanged()
            => SessionUsageChanged?.Invoke(this, EventArgs.Empty);

        private async Task<AiExecutorResult> ExecuteChatWithCommunicationStatusAsync(
            AIChatRequest request,
            AssistantConfiguration config,
            string statusText,
            CancellationToken ct)
        {
            RaiseCommunicationStatus(statusText);
            try
            {
                var provider = config.SelectedProvider;
                var pricing = provider == null ? null : await _pricingCatalogue.GetPricingAsync(
                    provider.ProviderType,
                    string.IsNullOrWhiteSpace(request.Model) ? provider.DefaultModel : request.Model,
                    ct).ConfigureAwait(false);
                _currentModelPricing = pricing;
                var budget = BuildTokenBudget(config);
                var outputReserve = Math.Max(budget.ReservedResponseTokens, config.MaxTokens ?? 0);
                EffectiveContextLimit = Math.Max(0, Math.Min(
                    budget.MaxContextTokens - outputReserve,
                    pricing?.MaxInputTokens ?? int.MaxValue));
                var isConversationRequest = request.Source != "FlowBloxAIAssistantSummary" &&
                    request.Source != "FlowBloxAIAssistantCompaction";
                if (outputReserve > 0)
                    request.MaxTokens = Math.Min(outputReserve, pricing?.MaxOutputTokens ?? int.MaxValue);
                var localEstimate = EstimateRequestTokens(request, budget);
                if (isConversationRequest)
                    CurrentContextTokens = localEstimate;
                var response = await _executor.ExecuteChatAsync(request, config, ct).ConfigureAwait(false);
                if (isConversationRequest && response.Usage.TotalInputTokens.HasValue)
                    CurrentContextTokens = (int)Math.Min(int.MaxValue, response.Usage.TotalInputTokens.Value);
                lock (_sessionSync)
                {
                    var session = GetOrCreateSession();
                    session.Usage.Add(response.Usage, pricing);
                    if (response.Usage.TotalInputTokens > 0 && localEstimate > 0 && isConversationRequest)
                    {
                        var observedMultiplier = budget.TokenEstimateMultiplier * response.Usage.TotalInputTokens.Value / localEstimate;
                        session.TokenEstimateMultiplier = Math.Max(session.TokenEstimateMultiplier, Math.Clamp(observedMultiplier, 1d, 4d));
                    }
                }
                RaiseSessionUsageChanged();
                DebugStateChanged?.Invoke(this, EventArgs.Empty);
                return response;
            }
            finally
            {
                RaiseCommunicationStatus(null);
            }
        }

        private void RaiseCommunicationStatus(string? text)
        {
            var args = string.IsNullOrWhiteSpace(text)
                ? AssistantCommunicationStatusChangedEventArgs.Hidden
                : new AssistantCommunicationStatusChangedEventArgs(text);

            CommunicationStatusChanged?.Invoke(this, args);
        }

        private static SnapshotAttachmentInformation ResolveSnapshotAttachmentInformation(
            bool attachJsonAutomatically,
            bool hasKnownJsonHash,
            bool jsonChanged)
        {
            if (attachJsonAutomatically)
            {
                if (!jsonChanged)
                    return SnapshotAttachmentInformation.UnchangedSinceLastConversation;

                return hasKnownJsonHash
                    ? SnapshotAttachmentInformation.ChangedSinceLastConversation
                    : SnapshotAttachmentInformation.InitialTransmission;
            }
            else
            {
                if (!hasKnownJsonHash)
                    return SnapshotAttachmentInformation.JsonDisabledInitialState;

                return jsonChanged
                    ? SnapshotAttachmentInformation.JsonDisabledStateChanged
                    : SnapshotAttachmentInformation.JsonDisabledStateUnchanged;
            }
        }

        private void UpdateKnownContextJsonHash(AssistantSessionState session)
        {
            ArgumentNullException.ThrowIfNull(session);

            session.LastContextJsonHash = ComputeJsonHash(ComponentContext?.CaptureJson() ?? GetCurrentProjectJson());
        }

        private static string GetCurrentProjectJson()
        {
            var project = FlowBloxProjectManager.Instance.ActiveProject;
            if (project == null)
                return "{}";

            return JsonConvert.SerializeObject(project, JsonSettings.ProjectExportForAiAssistant());
        }

        private static string SerializeToolTranscript(ToolRequest request, ToolResponse response)
        {
            var payload = JToken.FromObject(response);
            var hasVerbatimGuidance = response.RetainedContents.Any(content =>
                content.Kind == AssistantSystemMessageKind.Explanation || content.Kind == AssistantSystemMessageKind.ToolDefinition);
            var transcript = new JObject
            {
                ["tool"] = request.ToolName,
                ["arguments"] = CompactTypeAliasStrings(request.Arguments ?? new JObject()),
                ["response"] = hasVerbatimGuidance ? payload : CompactTypeAliasStrings(payload)
            };

            return transcript.ToString(Formatting.None);
        }

        private static JToken CompactTypeAliasStrings(JToken token)
        {
            var clone = token.DeepClone();
            CompactTypeAliasStringsInPlace(clone);
            return clone;
        }

        private static void CompactTypeAliasStringsInPlace(JToken token)
        {
            if (token is JValue { Type: JTokenType.String } valueToken)
            {
                var value = valueToken.Value<string>();
                if (!string.IsNullOrEmpty(value))
                    valueToken.Value = AiAssistantTypeAliasHelper.CompressTypeName(value);

                return;
            }

            foreach (var child in token.Children())
                CompactTypeAliasStringsInPlace(child);
        }

        private void AddTranscript(AssistantResult result, AssistantTranscriptKind kind, string text, string? internalContent = null)
        {
            var line = new AssistantTranscriptLine
            {
                Timestamp = DateTime.Now,
                Kind = kind,
                Text = text,
                InternalContent = internalContent ?? string.Empty
            };

            result.TranscriptLines.Add(line);
            TranscriptLineAdded?.Invoke(this, line);
        }

        private static string BuildToolExecutionTranscript(
            int requestedOperationCount,
            int successfulOperationCount,
            List<string> failedToolNames,
            List<ExecutedToolCallInfo> executedToolCalls)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Requested operations: {requestedOperationCount}");
            sb.AppendLine($"Executed operations: {executedToolCalls?.Count ?? 0}");
            sb.AppendLine($"Successful operations: {successfulOperationCount}");

            var hasAnySuccessful = successfulOperationCount > 0;
            var allSuccessful = requestedOperationCount > 0 && successfulOperationCount == requestedOperationCount;
            var status = allSuccessful
                ? "success"
                : hasAnySuccessful
                    ? "partial success"
                    : "issues detected";
            sb.AppendLine($"Status: {status}");

            if (!allSuccessful && failedToolNames?.Count > 0)
                sb.AppendLine("Failed tools: " + string.Join(", ", failedToolNames.Distinct(StringComparer.OrdinalIgnoreCase)));

            if (executedToolCalls == null || executedToolCalls.Count == 0)
                return sb.ToString().TrimEnd();

            sb.AppendLine();
            sb.AppendLine("Tool API outputs:");

            for (var i = 0; i < executedToolCalls.Count; i++)
            {
                var call = executedToolCalls[i];
                var response = call.Response ?? new ToolResponse();

                sb.AppendLine($"[{i + 1}] {call.ToolName}");
                sb.AppendLine("arguments:");
                sb.AppendLine((call.Arguments ?? new JObject()).ToString(Formatting.Indented));
                sb.AppendLine($"ok: {response.Ok}");

                if (!string.IsNullOrWhiteSpace(response.Error))
                {
                    sb.AppendLine("error:");
                    sb.AppendLine(response.Error);
                }

                if (response.Result != null && response.Result.HasValues)
                {
                    sb.AppendLine("result:");
                    sb.AppendLine(response.Result.ToString(Formatting.Indented));
                }

                if (response.Log != null && response.Log.HasValues)
                {
                    sb.AppendLine("log:");
                    sb.AppendLine(response.Log.ToString(Formatting.Indented));
                }

                if (i < executedToolCalls.Count - 1)
                    sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string BuildToolExecutionMessage(ToolExecutionSummary summary)
        {
            if (summary.AnySuccessful && !summary.AllSuccessful)
            {
                return $"{summary.SuccessfulOperationCount}/{summary.RequestedOperationCount} requested operations were executed successfully.";
            }

            if (summary.AllSuccessful)
            {
                if (summary.RequestedOperationCount == 1)
                    return "The requested operation was executed successfully.";

                return $"All {summary.RequestedOperationCount} requested operations were executed successfully.";
            }

            return "There was a problem with the request. A problem report was included.";
        }

        private static ToolExecutionSummary BuildToolExecutionSummary(List<ExecutedToolCallInfo> executedToolCalls)
        {
            var requestedOperationCount = 0;
            var successfulOperationCount = 0;
            var failedToolNames = new List<string>();

            if (executedToolCalls == null || executedToolCalls.Count == 0)
            {
                return new ToolExecutionSummary
                {
                    RequestedOperationCount = 0,
                    SuccessfulOperationCount = 0,
                    FailedToolNames = failedToolNames
                };
            }

            foreach (var executedToolCall in executedToolCalls)
            {
                var response = executedToolCall?.Response ?? new ToolResponse();
                var result = response.Result;

                if (result?["batchResults"] is JArray batchResults)
                {
                    var requestedBatchCount = executedToolCall.Arguments?["requests"] is JArray requests
                        ? requests.Count
                        : batchResults.Count;

                    var successfulBatchCount = batchResults
                        .OfType<JObject>()
                        .Count(x => x.Value<bool?>("ok") == true);

                    requestedOperationCount += requestedBatchCount;
                    successfulOperationCount += successfulBatchCount;

                    if (successfulBatchCount < requestedBatchCount)
                        failedToolNames.Add(executedToolCall.ToolName);

                    continue;
                }

                requestedOperationCount += 1;
                if (response.Ok)
                {
                    successfulOperationCount += 1;
                }
                else
                {
                    failedToolNames.Add(executedToolCall.ToolName);
                }
            }

            return new ToolExecutionSummary
            {
                RequestedOperationCount = requestedOperationCount,
                SuccessfulOperationCount = successfulOperationCount,
                FailedToolNames = failedToolNames
            };
        }

        private static string BuildToolProcessingTranscript(IReadOnlyList<AiResponseToolCall> toolCalls)
        {
            var sb = new StringBuilder();
            var calls = toolCalls ?? Array.Empty<AiResponseToolCall>();
            sb.AppendLine($"Requested operations: {calls.Count}");

            if (calls.Count == 0)
                return sb.ToString().TrimEnd();

            for (var i = 0; i < calls.Count; i++)
            {
                var call = calls[i];
                sb.AppendLine();
                sb.AppendLine($"[{i + 1}] {call.ToolName}");
                sb.AppendLine("arguments:");
                sb.AppendLine((call.Arguments ?? new JObject()).ToString(Formatting.Indented));
            }

            return sb.ToString().TrimEnd();
        }

        private static void EnsureAssistantActivityMessage(AiResponseInstruction instruction, JObject source)
        {
            if (instruction.Final || instruction.ToolCalls.Count == 0 || !string.IsNullOrWhiteSpace(instruction.AssistantMessage))
                return;

            instruction.AssistantMessage = AssistantActivityFormatter.Format(instruction.ToolCalls);
            var normalized = (JObject)source.DeepClone();
            normalized["assistantMessage"] = instruction.AssistantMessage;
            instruction.InternalContent = normalized.ToString(Formatting.Indented);
        }

        private static string BuildAssistantRoundMessage(AiResponseInstruction instruction, string assistantOutput)
        {
            if (!string.IsNullOrWhiteSpace(instruction?.AssistantMessage))
                return instruction.AssistantMessage;

            if (instruction?.ToolCalls?.Count > 0)
                return AssistantActivityFormatter.Format(instruction.ToolCalls);

            if (!string.IsNullOrWhiteSpace(assistantOutput))
                return assistantOutput;

            return "[no assistant message]";
        }

        private sealed class ExecutedToolCallInfo
        {
            public string ToolName { get; set; } = string.Empty;
            public JObject Arguments { get; set; } = new JObject();
            public ToolResponse Response { get; set; } = new ToolResponse();
        }

        private sealed class ToolExecutionSummary
        {
            public int RequestedOperationCount { get; set; }
            public int SuccessfulOperationCount { get; set; }
            public List<string> FailedToolNames { get; set; } = new();
            public bool AnySuccessful => SuccessfulOperationCount > 0;
            public bool AllSuccessful => RequestedOperationCount > 0 && SuccessfulOperationCount == RequestedOperationCount;
        }

        private sealed class AssistantSessionState
        {
            public List<AssistantRetainedContent> RetainedContents { get; } = new();
            public IReadOnlyList<AssistantSystemMessage>? InitialSystemMessages { get; set; }
            public bool RuntimeBindingsReset { get; set; }
            public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
            public string ConversationSummary { get; set; } = string.Empty;
            public string LastSummaryCompactionAttemptKey { get; set; } = string.Empty;
            public int SummarizedMessageCount { get; set; }
            public string LastContextJsonHash { get; set; } = string.Empty;
            public double TokenEstimateMultiplier { get; set; } = 1d;
            public AssistantSessionUsage Usage { get; set; } = new();
            public List<AssistantSessionMessage> Messages { get; set; } = new();
        }

    }
}
