using System.Text;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Services
{
    public class AiAssistantService
    {
        private const int AutomaticAdjustmentDelayMilliseconds = 500;
        internal const string ContinuationModelPrompt =
            "Continue from the latest stored Tool API response in the conversation history. " +
            "Return exactly one assistant JSON object using the required schema. " +
            "Do not write prose outside the JSON object.";

        private readonly IAiExecutor _executor;
        private readonly IFlowBloxAIToolApi _tools;
        private readonly IAiResponseInstructionParserService _instructionParser;
        private readonly AssistantOutputFormatFeedbackQueue _outputFormatFeedbackQueue;
        private readonly ILogger? _logger;
        private readonly Func<AssistantConfiguration>? _configurationProvider;
        private readonly object _sessionSync = new();
        private int _activeRunCount;
        private AssistantSessionState? _session;

        public event EventHandler<AssistantTranscriptLine>? TranscriptLineAdded;
        public event EventHandler<int>? EstimatedUsedTokensChanged;
        public event EventHandler<AssistantCommunicationStatusChangedEventArgs>? CommunicationStatusChanged;
        public event EventHandler? DebugStateChanged;
        public event EventHandler? ConversationSummaryChanged;
        public event EventHandler? BeforeAutomaticLayoutAdjustment;

        public AiAssistantService(
            IAiExecutor executor,
            IFlowBloxAIToolApi tools,
            ILogger? logger = null,
            Func<AssistantConfiguration>? configurationProvider = null)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _instructionParser = FlowBloxServiceLocator.Instance.GetService<IAiResponseInstructionParserService>();
            _outputFormatFeedbackQueue = new AssistantOutputFormatFeedbackQueue();
            _logger = logger;
            _configurationProvider = configurationProvider;
        }

        public void ResetSession()
        {
            var estimatedUsedTokens = 0;
            lock (_sessionSync)
            {
                if (_session != null)
                    ToolHandlerUtilities.ClearSessionCache(_session.SessionId);

                _session = null;
            }

            RaiseEstimatedUsedTokensChanged(estimatedUsedTokens);
            ConversationSummaryChanged?.Invoke(this, EventArgs.Empty);
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }


        public void RestoreSession(AiAssistantHistoryDocument history)
        {
            lock (_sessionSync)
            {
                if (_session != null)
                    ToolHandlerUtilities.ClearSessionCache(_session.SessionId);

                var session = new AssistantSessionState
                {
                    ConversationSummary = history?.ConversationSummary ?? string.Empty,
                    SummarizedMessageCount = Math.Max(0, history?.SummarizedMessageCount ?? 0),
                    LastProjectJsonHash = history?.LastProjectJsonHash ?? string.Empty,
                    EstimatedUsedTokens = Math.Max(0, history?.EstimatedUsedTokens ?? 0)
                };

                var sessionMessages = history?.SessionMessages?
                    .Where(x => !string.IsNullOrWhiteSpace(x?.CompleteMessage))
                    .Select(x => x.Clone())
                    .ToList() ?? new List<AssistantSessionMessage>();

                session.Messages.AddRange(sessionMessages);
                session.SummarizedMessageCount = Math.Clamp(session.SummarizedMessageCount, 0, session.Messages.Count);
                session.RuntimeBindingsReset = session.Messages.Count > 0 || !string.IsNullOrWhiteSpace(session.ConversationSummary);
                _session = session;
                RaiseEstimatedUsedTokensChanged(session.EstimatedUsedTokens);
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
                history.LastProjectJsonHash = session.LastProjectJsonHash;
                history.ConversationSummary = session.ConversationSummary;
                history.SummarizedMessageCount = session.SummarizedMessageCount;
                history.EstimatedUsedTokens = session.EstimatedUsedTokens;
                history.LoadedExplanationKeys = []; // Ignore legacy persisted runtime bindings.
                history.SessionMessages = session.Messages
                    .Where(x => !string.IsNullOrWhiteSpace(x?.CompleteMessage))
                    .Select(x => x.Clone())
                    .ToList();
            }
        }

        public int EstimatedUsedTokens
        {
            get
            {
                lock (_sessionSync)
                    return Math.Max(0, _session?.EstimatedUsedTokens ?? 0);
            }
        }

        public string CurrentConversationSummary
        {
            get
            {
                lock (_sessionSync)
                    return _session?.ConversationSummary ?? string.Empty;
            }
        }

        public void ResetEstimatedUsedTokens()
        {
            int value;
            lock (_sessionSync)
            {
                var session = GetOrCreateSession();
                session.EstimatedUsedTokens = 0;
                value = session.EstimatedUsedTokens;
            }

            RaiseEstimatedUsedTokensChanged(value);
        }

        public AssistantConfiguration GetConfiguration(out string error)
        {
            error = string.Empty;
            if (_configurationProvider != null)
                return _configurationProvider() ?? throw new InvalidOperationException("Assistant configuration provider returned null.");

            var rawConfig = FlowBloxOptions.GetOptionInstance().GetOption("AI.AssistantConfiguration")?.Value ?? string.Empty;
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
                var option = options.GetOption("AI.AssistantConfiguration");
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

        private static AssistantTokenBudget BuildTokenBudget(AssistantConfiguration config)
        {
            ArgumentNullException.ThrowIfNull(config);

            return new AssistantTokenBudget
            {
                MaxContextTokens = Math.Max(AssistantConfigurationLimits.MinContextTokens, config.MaxContextTokens),
                ReservedResponseTokens = Math.Max(AssistantConfigurationLimits.MinReservedResponseTokens, config.ReservedResponseTokens),
                ApproximateCharactersPerToken = Math.Clamp(
                    config.ApproximateCharactersPerToken,
                    AssistantConfigurationLimits.MinApproximateCharactersPerToken,
                    AssistantConfigurationLimits.MaxApproximateCharactersPerToken)
            };
        }

        public async Task<AssistantResult> GenerateProjectAsync(string userPrompt, CancellationToken ct, AssistantRunControl? runControl = null)
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

            var project = FlowBloxProjectManager.Instance.ActiveProject;
            using var registryScope = project == null
                ? null
                : FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);

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
            var tokenBudget = BuildTokenBudget(config);
            var session = GetOrCreateSession();
            _outputFormatFeedbackQueue.ClearIncorrectOutputFormatData();
            ToolHandlerUtilities.SetCurrentSessionGuid(session.SessionId);
            var currentProjectJson = GetCurrentProjectJson();
            var currentProjectJsonHash = ComputeProjectJsonHash(currentProjectJson);
            var hasKnownProjectJsonHash = !string.IsNullOrWhiteSpace(session.LastProjectJsonHash);
            var projectJsonChanged = !string.Equals(session.LastProjectJsonHash, currentProjectJsonHash, StringComparison.OrdinalIgnoreCase);
            var shouldAttachProjectJson = config.AttachProjectJsonAutomatically && projectJsonChanged;
            var projectAttachmentInformation = ResolveProjectAttachmentInformation(
                config.AttachProjectJsonAutomatically,
                hasKnownProjectJsonHash,
                projectJsonChanged);
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
                            shouldAttachProjectJson ? currentProjectJson : null,
                            projectAttachmentInformation,
                            ToolHandlerUtilities.GetActiveAuxiliaryProjectName())
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
                        config.Provider,
                        tokenBudget,
                        session.SummarizedMessageCount,
                        GetRuntimeSystemContents(session.SessionId));
                    summaryTargetMessageCount = Math.Max(
                        summaryTargetMessageCount,
                        CalculateSummaryCompactionTarget(
                            session,
                            chatRequestResult.FirstIncludedHistoryMessageIndex,
                            minLatestMessages,
                            summaryCompactionRate));
                    await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, ct).ConfigureAwait(false);

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
                        config.Provider,
                        tokenBudget,
                        session.SummarizedMessageCount,
                        GetRuntimeSystemContents(session.SessionId));
                    var chatRequest = chatRequestResult.Request;
                    if (toolRound == 1)
                        AppendSingleSessionMessage(session, "user", initialUserPrompt);

                    var exec = await ExecuteChatWithCommunicationStatusAsync(
                        chatRequest,
                        config,
                        "Thinking...",
                        ct).ConfigureAwait(false);
                    AddEstimatedTokenUsage(session, EstimateTokenUsage(chatRequest, exec, tokenBudget, config.Provider));
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

                    var parseResult = _instructionParser.Parse(assistantOutput, config.Provider);
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
                        UpdateKnownProjectJsonHash(session);
                        await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, ct).ConfigureAwait(false);
                        return result;
                    }

                    formatRetryIssued = false;

                    _outputFormatFeedbackQueue.ClearIncorrectOutputFormatData();
                    var instruction = parseResult.Instruction;
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
                        UpdateKnownProjectJsonHash(session);
                        await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, ct).ConfigureAwait(false);

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

                        // A later call may promote declarations listed by an earlier call in this same round.
                        // Re-render metadata after all calls, without executing handlers or counting again.
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
                            ToolHandlerUtilities.GetActiveAuxiliaryProjectName());
                        AppendMessagePair(session, assistantInstructionContent, toolApiResponse, exec.MessageMetadata);
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
                UpdateKnownProjectJsonHash(session);
                await TryUpdateConversationSummaryAsync(session, summaryTargetMessageCount, config, ct).ConfigureAwait(false);
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
                    .GetOption("AI.CommuncationProtocolDir")?
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

        private static IReadOnlyList<string> GetRuntimeSystemContents(string sessionId) =>
            AssistantSessionContentStore.Get(sessionId).GetSystemMessages().Select(x => x.Content).ToArray();

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
            var configuration = GetConfiguration(out _);
            lock (_sessionSync)
            {
                var session = GetOrCreateSession();
                session.InitialSystemMessages ??= CreateInitialSystemMessages(
                    AssistantPromptBuilder.BuildSystemPrompt(),
                    AssistantPromptBuilder.BuildSessionBootstrapPrompt(_tools.GetToolDefinitions(), session.RuntimeBindingsReset));
                var systemMessages = session.InitialSystemMessages
                    .Concat(AssistantSessionContentStore.Get(session.SessionId).GetSystemMessages())
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
                    BuildTokenBudget(configuration).ApproximateCharactersPerToken,
                    Math.Clamp(
                        configuration.Provider?.EstimatedSystemPromptCacheSavingsRate ?? 0d,
                        0d,
                        1d));
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
            AIChatMessageMetadata? metadata)
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
                    Metadata = metadata?.Clone() ?? new AIChatMessageMetadata()
                });
            }
            DebugStateChanged?.Invoke(this, EventArgs.Empty);
        }

        private async Task TryUpdateConversationSummaryAsync(
            AssistantSessionState session,
            int targetSummarizedMessageCount,
            AssistantConfiguration config,
            CancellationToken ct)
        {
            if (session == null || session.Messages.Count == 0)
                return;

            try
            {
                string currentSummary;
                List<AssistantSessionMessage> messagesToSummarize;

                lock (_sessionSync)
                {
                    targetSummarizedMessageCount = Math.Clamp(targetSummarizedMessageCount, 0, session.Messages.Count);
                    if (targetSummarizedMessageCount <= session.SummarizedMessageCount)
                        return;

                    currentSummary = session.ConversationSummary;
                    messagesToSummarize = session.Messages
                        .Skip(session.SummarizedMessageCount)
                        .Take(targetSummarizedMessageCount - session.SummarizedMessageCount)
                        .ToList();
                }

                if (messagesToSummarize.Count == 0)
                    return;

                var summaryRequest = AssistantSummaryRequestBuilder.Build(
                    currentSummary,
                    messagesToSummarize,
                    config.SummarizationStyle);
                var tokenBudget = BuildTokenBudget(config);
                var summaryResult = await ExecuteChatWithCommunicationStatusAsync(
                    summaryRequest,
                    config,
                    "Summarize...",
                    ct).ConfigureAwait(false);
                AddEstimatedTokenUsage(session, EstimateTokenUsage(summaryRequest, summaryResult, tokenBudget, config.Provider));
                if (!summaryResult.Success || string.IsNullOrWhiteSpace(summaryResult.OutputText))
                {
                    if (!string.IsNullOrWhiteSpace(summaryResult.Error))
                        _logger?.Warn($"AI Assistant summary update failed: {summaryResult.Error}");

                    return;
                }

                lock (_sessionSync)
                {
                    session.ConversationSummary = summaryResult.OutputText.Trim();
                    session.SummarizedMessageCount = Math.Max(session.SummarizedMessageCount, targetSummarizedMessageCount);
                }
                ConversationSummaryChanged?.Invoke(this, EventArgs.Empty);
                DebugStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.Warn($"AI Assistant summary update failed: {ex.Message}");
            }
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

        private static string ComputeProjectJsonHash(string projectJson)
        {
            return HashHelper.ComputeSHA256Hash(Encoding.UTF8.GetBytes(projectJson ?? string.Empty));
        }

        private void AddEstimatedTokenUsage(AssistantSessionState session, int tokenCount)
        {
            if (session == null || tokenCount <= 0)
                return;

            int newValue;
            lock (_sessionSync)
            {
                session.EstimatedUsedTokens = Math.Max(0, session.EstimatedUsedTokens + tokenCount);
                newValue = session.EstimatedUsedTokens;
            }

            RaiseEstimatedUsedTokensChanged(newValue);
        }

        private static int EstimateTokenUsage(
            AIChatRequest request,
            AiExecutorResult result,
            AssistantTokenBudget tokenBudget,
            AIProviderBase? provider)
        {
            if (tokenBudget == null)
                return 0;

            var promptTokens = EstimateRequestTokens(
                request,
                tokenBudget,
                provider?.EstimatedSystemPromptCacheSavingsRate ?? 0d);
            var completionTokens = result?.CompletionTokens ??
                                   tokenBudget.EstimateTokens(result?.OutputText ?? result?.RawOutput ?? string.Empty);

            return Math.Max(0, promptTokens) + Math.Max(0, completionTokens);
        }

        private static int EstimateRequestTokens(
            AIChatRequest request,
            AssistantTokenBudget tokenBudget,
            double estimatedSystemPromptCacheSavingsRate)
        {
            if (request == null || tokenBudget == null)
                return 0;

            var tokens = 0;
            foreach (var message in request.SystemMessages ?? Enumerable.Empty<AIChatMessage>())
                tokens += EstimateEffectiveSystemMessageTokens(message, tokenBudget, estimatedSystemPromptCacheSavingsRate);

            foreach (var message in request.Messages ?? Enumerable.Empty<AIChatMessage>())
                tokens += tokenBudget.EstimateTokens(message?.Content);

            return tokens;
        }

        private static int EstimateEffectiveSystemMessageTokens(
            AIChatMessage message,
            AssistantTokenBudget tokenBudget,
            double estimatedSystemPromptCacheSavingsRate)
        {
            var tokens = tokenBudget.EstimateTokens(message?.Content);
            if (tokens <= 0 || message?.CacheBehavior != AIChatCacheBehavior.PreferCache)
                return tokens;

            var savingsRate = Math.Clamp(estimatedSystemPromptCacheSavingsRate, 0d, 1d);
            return Math.Max(0, (int)Math.Ceiling(tokens * (1d - savingsRate)));
        }

        private void RaiseEstimatedUsedTokensChanged(int estimatedUsedTokens)
            => EstimatedUsedTokensChanged?.Invoke(this, Math.Max(0, estimatedUsedTokens));

        private async Task<AiExecutorResult> ExecuteChatWithCommunicationStatusAsync(
            AIChatRequest request,
            AssistantConfiguration config,
            string statusText,
            CancellationToken ct)
        {
            RaiseCommunicationStatus(statusText);
            try
            {
                return await _executor.ExecuteChatAsync(request, config, ct).ConfigureAwait(false);
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

        private static ProjectAttachmentInformation ResolveProjectAttachmentInformation(
            bool attachProjectJsonAutomatically,
            bool hasKnownProjectJsonHash,
            bool projectJsonChanged)
        {
            if (attachProjectJsonAutomatically)
            {
                if (!projectJsonChanged)
                    return ProjectAttachmentInformation.ProjectUnchangedSinceLastConversation;

                return hasKnownProjectJsonHash
                    ? ProjectAttachmentInformation.ProjectChangedSinceLastConversation
                    : ProjectAttachmentInformation.InitialTransmission;
            }
            else
            {
                if (!hasKnownProjectJsonHash)
                    return ProjectAttachmentInformation.ProjectJsonDisabledInitialState;

                return projectJsonChanged
                    ? ProjectAttachmentInformation.ProjectJsonDisabledProjectChanged
                    : ProjectAttachmentInformation.ProjectJsonDisabledProjectUnchanged;
            }
        }

        private static void UpdateKnownProjectJsonHash(AssistantSessionState session)
        {
            ArgumentNullException.ThrowIfNull(session);

            session.LastProjectJsonHash = ComputeProjectJsonHash(GetCurrentProjectJson());
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
            var transcript = new JObject
            {
                ["tool"] = request.ToolName,
                ["arguments"] = CompactTypeAliasStrings(request.Arguments ?? new JObject()),
                ["response"] = CompactTypeAliasStrings(JToken.FromObject(response))
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

        private static string BuildAssistantRoundMessage(AiResponseInstruction instruction, string assistantOutput)
        {
            if (!string.IsNullOrWhiteSpace(instruction?.AssistantMessage))
                return instruction.AssistantMessage;

            if (instruction?.ToolCalls?.Count > 0)
            {
                var toolNames = instruction.ToolCalls
                    .Select(x => x.ToolName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (toolNames.Count > 0)
                    return $"Executing {instruction.ToolCalls.Count} operation(s): {string.Join(", ", toolNames)}";

                return $"Executing {instruction.ToolCalls.Count} operation(s).";
            }

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
            public IReadOnlyList<AssistantSystemMessage>? InitialSystemMessages { get; set; }
            public bool RuntimeBindingsReset { get; set; }
            public string SessionId { get; set; } = Guid.NewGuid().ToString("N");
            public string ConversationSummary { get; set; } = string.Empty;
            public int SummarizedMessageCount { get; set; }
            public string LastProjectJsonHash { get; set; } = string.Empty;
            public int EstimatedUsedTokens { get; set; }
            public List<AssistantSessionMessage> Messages { get; set; } = new();
        }

    }
}
