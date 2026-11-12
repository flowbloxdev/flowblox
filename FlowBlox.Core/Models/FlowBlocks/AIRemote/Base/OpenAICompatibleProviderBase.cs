using FlowBlox.Core.Extensions;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Enums;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Base
{
    [PluralDisplayName("OpenAICompatibleProviderBase_DisplayName_Plural", typeof(FlowBloxTexts))]
    public abstract class OpenAICompatibleProviderBase : AIProviderBase
    {
        protected abstract string ProviderDisplayName { get; }

        protected virtual string? OrganizationIdForRequest => null;

        protected override bool SupportsReasoningEffort => true;

        protected OpenAICompatibleProviderBase(string baseUrl, string defaultModel)
        {
            BaseUrl = baseUrl;
            DefaultModel = defaultModel;
        }

        protected override async Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
        {
            var apiKey = this.Resolve(owner => owner.ApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AIResponse
                {
                    Success = false,
                    Error = $"{ProviderDisplayName} API key is empty after field resolution."
                };
            }

            var model = FlowBloxFieldHelper.ReplaceFieldsInString(request.Model);
            if (string.IsNullOrWhiteSpace(model))
                model = this.Resolve(owner => owner.DefaultModel);

            var baseUrl = this.Resolve(owner => owner.BaseUrl, required: true);

            var organizationId = this.Resolve(x => x.OrganizationIdForRequest);
            var timeoutSeconds = ResolveTimeoutSeconds(request);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

#pragma warning disable SKEXP0010
            var chatService = string.Equals(baseUrl.TrimEnd('/'), "https://api.openai.com/v1", StringComparison.OrdinalIgnoreCase)
                ? new OpenAIChatCompletionService(model, apiKey, organizationId, httpClient, loggerFactory: null)
                : new OpenAIChatCompletionService(model, new Uri(baseUrl.TrimEnd('/')), apiKey, organizationId, httpClient, loggerFactory: null);
#pragma warning restore SKEXP0010

            var response = await SendChatMessageContentAsync(
                chatService,
                request,
                AIChatHistoryBuilder.Build(request),
                BuildExecutionSettings(request),
                ct).ConfigureAwait(false);

            var usage = new OpenAICompatibleUsageReporter(ProviderDisplayName).ReportUsage(response);

            return new AIResponse
            {
                Success = true,
                Text = response?.Content ?? string.Empty,
                PromptTokens = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens
            };
        }

        protected virtual OpenAIPromptExecutionSettings BuildExecutionSettings(AIChatRequest request)
        {
            var settings = new OpenAIPromptExecutionSettings();
            if (request.Temperature is >= 0 and <= 2)
                settings.Temperature = request.Temperature;

            if (request.MaxTokens is > 0)
                settings.MaxTokens = request.MaxTokens;

            if (GetRuntimeReasoningEffort() != AIReasoningEffort.None)
                settings.ReasoningEffort = GetReasoningEffortValue();

            ConfigureExecutionSettings(settings, request);

            return settings;
        }

        protected virtual string GetReasoningEffortValue()
        {
            return ToReasoningEffortValue(GetRuntimeReasoningEffort());
        }

        protected virtual void ConfigureExecutionSettings(OpenAIPromptExecutionSettings settings, AIChatRequest request)
        {
        }

        protected virtual Task<ChatMessageContent> SendChatMessageContentAsync(
            IChatCompletionService chatService,
            AIChatRequest request,
            ChatHistory chatHistory,
            OpenAIPromptExecutionSettings executionSettings,
            CancellationToken ct)
        {
            return chatService.GetChatMessageContentAsync(
                chatHistory,
                executionSettings,
                kernel: null,
                cancellationToken: ct);
        }
    }
}
