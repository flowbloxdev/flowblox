using FlowBlox.Core.Extensions;
using FlowBlox.Core.Util.Fields;
using SkiaSharp;
using System.ComponentModel.DataAnnotations;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Base
{
    [Display(Name = "AIProviderBase_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("AIProviderBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    [FlowBloxUIGroup("Global_Groups_Requirements", hide: true)]
    public abstract class AIProviderBase : ManagedObject, IAIProvider
    {
        public override SKImage Icon16 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.robot_outline, 16, new SKColor(3, 105, 161));

        public override SKImage Icon32 => FlowBloxIconUtil.CreateFromSVG(FlowBloxIcons.robot_outline, 32, new SKColor(3, 105, 161));
        [Required]
        [Display(Name = "AIProvider_ApiKey", Description = "AIProvider_ApiKey_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 0)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        [FlowBloxTextBox]
        public string ApiKey { get; set; }

        [Display(Name = "AIProvider_DefaultModel", Description = "AIProvider_DefaultModel_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 1)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string DefaultModel { get; set; }

        [Display(Name = "AIProvider_EstimatedSystemPromptCacheSavingsRate", Description = "AIProvider_EstimatedSystemPromptCacheSavingsRate_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 2)]
        [Range(0d, 1d)]
        public double EstimatedSystemPromptCacheSavingsRate { get; set; }

        [Display(Name = "AIProvider_ReasoningEffort", Description = "AIProvider_ReasoningEffort_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 3)]
        [ActivationCondition(ActivationMethod = nameof(IsReasoningEffortActive))]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public AIReasoningEffort ReasoningEffort { get; set; }

        [Display(Name = "AIProvider_TimeoutSeconds", Description = "AIProvider_TimeoutSeconds_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 4)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public int TimeoutSeconds { get; set; }

        [Display(Name = "AIProvider_BaseUrl", Description = "AIProvider_BaseUrl_Tooltip", ResourceType = typeof(FlowBloxTexts), Order = 5)]
        [FlowBloxUI(UiOptions = UIOptions.EnableFieldSelection)]
        public string BaseUrl { get; set; }

        public abstract string ProviderType { get; }

        protected virtual bool SupportsReasoningEffort => false;

        public virtual bool SupportsNativeFunctionCallHistory => true;

        protected AIProviderBase()
        {
            ReasoningEffort = AIReasoningEffort.Medium;
            TimeoutSeconds = AiProviderDefaults.DefaultTimeoutSeconds;
        }

        public Task<AIResponse> ExecuteChatAsync(AIChatRequest request, CancellationToken ct)
        {
            return ExecuteChatAsyncInternal(runtime: null, request, ct);
        }

        public async Task<AIResponse> ExecuteChatAsync(BaseRuntime runtime, AIChatRequest request, CancellationToken ct)
        {
            return await ExecuteChatAsyncInternal(runtime, request, ct).ConfigureAwait(false);
        }

        public Task<AIResponse> ExecuteAsync(AIRequest request, CancellationToken ct)
        {
            return ExecuteAsyncInternal(runtime: null, request, ct);
        }

        public async Task<AIResponse> ExecuteAsync(BaseRuntime runtime, AIRequest request, CancellationToken ct)
        {
            return await ExecuteAsyncInternal(runtime, request, ct).ConfigureAwait(false);
        }

        private async Task<AIResponse> ExecuteAsyncInternal(BaseRuntime runtime, AIRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Prompt))
            {
                return new AIResponse
                {
                    Success = false,
                    Error = "Prompt is empty."
                };
            }

            if (string.IsNullOrWhiteSpace(request.Model))
                request.Model = DefaultModel;

            var chatRequest = new AIChatRequest
            {
                Model = request.Model,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
                TimeoutSecondsOverride = request.TimeoutSecondsOverride,
                Source = request.Meta.TryGetValue("Source", out var source) ? source?.ToString() ?? string.Empty : string.Empty,
                Meta = request.Meta
            };

            if (!string.IsNullOrWhiteSpace(request.SystemInstruction))
            {
                chatRequest.SystemMessages.Add(new AIChatMessage
                {
                    Role = "system",
                    Content = request.SystemInstruction
                });
            }

            chatRequest.Messages.Add(new AIChatMessage
            {
                Role = "user",
                Content = request.Prompt
            });

            return await ExecuteChatAsyncInternal(runtime, chatRequest, ct).ConfigureAwait(false);
        }

        private async Task<AIResponse> ExecuteChatAsyncInternal(BaseRuntime runtime, AIChatRequest request, CancellationToken ct)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if ((request.Messages == null || request.Messages.Count == 0) &&
                (request.SystemMessages == null || request.SystemMessages.Count == 0))
            {
                return new AIResponse
                {
                    Success = false,
                    Error = "Chat request is empty."
                };
            }

            if (string.IsNullOrWhiteSpace(request.Model))
                request.Model = DefaultModel;

            var timeoutSeconds = ResolveTimeoutSeconds(request);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                return await ExecuteChatCoreAsync(request, cts.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                runtime?.Report($"AI request cancelled or timed out.", FlowBloxLogLevel.Error, ex);
                var innerMessage = ex.InnerException?.Message;

                if (ct.IsCancellationRequested)
                {
                    return new AIResponse
                    {
                        Success = false,
                        Error = string.IsNullOrWhiteSpace(innerMessage)
                            ? "AI request was cancelled."
                            : $"AI request was cancelled. Details: {innerMessage}"
                    };
                }
                else
                {
                    return new AIResponse
                    {
                        Success = false,
                        Error = string.IsNullOrWhiteSpace(innerMessage)
                            ? $"AI request timed out after {timeoutSeconds}s."
                            : $"AI request timed out after {timeoutSeconds}s. Details: {innerMessage}"
                    };
                }
            }
            catch (HttpRequestException ex)
            {
                runtime?.Report($"HTTP error during AI request.", FlowBloxLogLevel.Error, ex);

                return new AIResponse
                {
                    Success = false,
                    Error = ex.Message
                };
            }
            catch (Exception ex)
            {
                runtime?.Report($"Unexpected error during AI request.", FlowBloxLogLevel.Error, ex);

                return new AIResponse
                {
                    Success = false,
                    Error = ex.ToString()
                };
            }
        }

        public override void RuntimeStarted(BaseRuntime runtime)
        {
            OnBeforeExecution();
            base.RuntimeStarted(runtime);
        }

        public override void RuntimeFinished(BaseRuntime runtime)
        {
            OnAfterExecution();
            base.RuntimeFinished(runtime);
        }

        public void PrepareExecution()
        {
            OnBeforeExecution();
        }

        public void CompleteExecution()
        {
            OnAfterExecution();
        }

        protected virtual void OnBeforeExecution()
        {
        }

        protected virtual void OnAfterExecution()
        {
        }

        public bool IsReasoningEffortActive() => SupportsReasoningEffort;

        public virtual IReadOnlyDictionary<string, object>? BuildChatMessageMetadata(AIChatMessageMetadata? metadata)
        {
            return null;
        }

        public virtual bool TryParseInstruction(
            string output,
            Exception? primaryParseException,
            out JObject instructionJson)
        {
            instructionJson = new JObject();
            return false;
        }

        protected int ResolveTimeoutSeconds(AIChatRequest request)
        {
            var timeoutSeconds = request?.TimeoutSecondsOverride
                ?? this.Resolve(provider => provider.TimeoutSeconds);
            return timeoutSeconds > 0
                ? timeoutSeconds
                : AiProviderDefaults.DefaultTimeoutSeconds;
        }

        protected AIReasoningEffort GetRuntimeReasoningEffort() =>
            this.Resolve(provider => provider.ReasoningEffort);

        protected static string ToReasoningEffortValue(AIReasoningEffort reasoningEffort)
        {
            return reasoningEffort switch
            {
                AIReasoningEffort.Low => "low",
                AIReasoningEffort.Medium => "medium",
                AIReasoningEffort.High => "high",
                _ => throw new InvalidOperationException(
                    $"Reasoning effort '{reasoningEffort}' does not have a provider request value.")
            };
        }

        protected abstract Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct);
    }
}
