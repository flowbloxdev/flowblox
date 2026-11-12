using FlowBlox.Core.Extensions;
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Util.Fields;
using Microsoft.SemanticKernel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers
{
    [Display(Name = "AnthropicAIProvider_DisplayName", ResourceType = typeof(FlowBloxTexts))]
    [PluralDisplayName("AnthropicAIProvider_DisplayName_Plural", typeof(FlowBloxTexts))]
    public sealed class AnthropicAIProvider : AIProviderBase
    {
        public override string ProviderType => "Anthropic";

        public AnthropicAIProvider()
        {
            BaseUrl = "https://api.anthropic.com/v1";
            DefaultModel = "claude-opus-5-5";
            EstimatedSystemPromptCacheSavingsRate = 0.70d;
        }

        protected override async Task<AIResponse> ExecuteChatCoreAsync(AIChatRequest request, CancellationToken ct)
        {
            var apiKey = this.Resolve(owner => owner.ApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new AIResponse
                {
                    Success = false,
                    Error = "Anthropic API key is empty after field resolution."
                };
            }

            var baseUrl = this.Resolve(owner => owner.BaseUrl, required: true);

            var model = FlowBloxFieldHelper.ReplaceFieldsInString(request.Model);
            if (string.IsNullOrWhiteSpace(model))
                model = this.Resolve(owner => owner.DefaultModel);

            var timeoutSeconds = ResolveTimeoutSeconds(request);
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
            var apiUrlFormat = BuildApiUrlFormat(baseUrl);
            using var client = new AnthropicClient(new APIAuthentication(apiKey), httpClient, requestInterceptor: null)
            {
                ApiUrlFormat = apiUrlFormat
            };

            var messageParameters = BuildMessageParameters(request, model);

            MessageResponse response;
            try
            {
                response = await client
                    .Messages
                    .GetClaudeMessageAsync(messageParameters, ct)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new HttpRequestException(
                    $"Anthropic request failed. Model: '{model}', BaseUrl: '{baseUrl}', ApiUrlFormat: '{apiUrlFormat}'. Details: {ex.Message}",
                    ex,
                    ex.StatusCode);
            }

            var responseText = response?.Message?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(responseText) &&
                string.Equals(Convert.ToString(response?.StopReason), "max_tokens", StringComparison.OrdinalIgnoreCase))
            {
                return new AIResponse
                {
                    Success = false,
                    Error =
                        $"Anthropic returned no text because the response stopped at the max_tokens limit ({messageParameters.MaxTokens}). " +
                        "Claude may have used the complete output budget for thinking before producing the required JSON response. " +
                        "Increase the assistant MaxTokens setting and retry."
                };
            }

            var usage = new AnthropicUsageReporter().ReportUsage(response);

            return new AIResponse
            {
                Success = true,
                Text = responseText,
                PromptTokens = usage.PromptTokens,
                CompletionTokens = usage.CompletionTokens
            };
        }

        private static string BuildApiUrlFormat(string baseUrl)
        {
            var trimmedBaseUrl = baseUrl.Trim().TrimEnd('/');

            if (trimmedBaseUrl.Contains("{0}", StringComparison.Ordinal) &&
                trimmedBaseUrl.Contains("{1}", StringComparison.Ordinal))
                return trimmedBaseUrl;

            if (trimmedBaseUrl.Contains("{0}", StringComparison.Ordinal))
                return trimmedBaseUrl.Replace("{0}", "{0}/{1}", StringComparison.Ordinal);

            var versionSuffix = Regex.Match(trimmedBaseUrl, @"/v\d+$", RegexOptions.IgnoreCase);
            if (versionSuffix.Success)
                return trimmedBaseUrl.Substring(0, versionSuffix.Index) + "/{0}/{1}";

            return trimmedBaseUrl + "/{0}/{1}";
        }

        private static MessageParameters BuildMessageParameters(AIChatRequest request, string model)
        {
            var parameters = new MessageParameters
            {
                Model = model,
                MaxTokens = request.MaxTokens.GetValueOrDefault(AiProviderDefaults.Anthropic.DefaultMaxTokens),
                Stream = false,
                System = BuildSystemMessages(request),
                Messages = BuildMessages(request),
                PromptCaching = PromptCacheType.FineGrained
            };

            // Opus 5.5 rejects sampling parameters; preserve temperature for older models.
            if (!model.StartsWith("claude-opus-5-5", StringComparison.OrdinalIgnoreCase) &&
                request.Temperature is >= 0 and <= 1)
                parameters.Temperature = (decimal)request.Temperature.Value;

            return parameters;
        }

        private static List<SystemMessage> BuildSystemMessages(AIChatRequest request)
        {
            var systemMessages = new List<SystemMessage>();

            foreach (var systemMessage in request?.SystemMessages ?? Enumerable.Empty<AIChatMessage>())
            {
                if (string.IsNullOrWhiteSpace(systemMessage?.Content))
                    continue;

                var content = systemMessage.Content.Trim();
                if (systemMessage.CacheBehavior == AIChatCacheBehavior.PreferCache)
                    systemMessages.Add(new SystemMessage(content, new CacheControl { Type = CacheControlType.ephemeral }));
                else
                    systemMessages.Add(new SystemMessage(content));
            }

            return systemMessages;
        }

        private static List<Message> BuildMessages(AIChatRequest request)
        {
            var messages = new List<Message>();

            foreach (var message in request?.Messages ?? Enumerable.Empty<AIChatMessage>())
            {
                if (message?.SemanticContent == null && string.IsNullOrWhiteSpace(message?.Content))
                    continue;

                var role = ToAnthropicRole(message.Role);
                var anthropicMessage = new Message(role, message.Content?.Trim() ?? string.Empty)
                {
                    Content = ToAnthropicContent(message)
                };

                messages.Add(anthropicMessage);
            }

            return messages;
        }

        private static RoleType ToAnthropicRole(string role)
        {
            return string.Equals(role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? RoleType.Assistant
                : RoleType.User;
        }

        private static List<ContentBase> ToAnthropicContent(AIChatMessage message)
        {
            List<ContentBase> content;
            if (message.SemanticContent == null)
            {
                content = ToAnthropicTextContent(message.Content);
            }
            else
            {
                var contentItems = new List<ContentBase>();
                foreach (var item in message.SemanticContent.Items)
                {
                    switch (item)
                    {
                        case FunctionCallContent functionCall:
                            contentItems.Add(new ToolUseContent
                            {
                                Id = functionCall.Id,
                                Name = functionCall.FunctionName,
                                Input = ToJsonNode(ToAnthropicArguments(functionCall.Arguments ?? new KernelArguments()))
                            });
                            break;

                        case FunctionResultContent functionResult:
                            contentItems.Add(new ToolResultContent
                            {
                                ToolUseId = functionResult.CallId,
                                Content = ToAnthropicTextContent(functionResult.Result?.ToString() ?? string.Empty)
                            });
                            break;
                    }
                }

                content = contentItems.Count > 0
                    ? contentItems
                    : ToAnthropicTextContent(message.Content);
            }

            if (message.CacheBehavior == AIChatCacheBehavior.PreferCache)
                ApplyCacheControl(content);

            return content;
        }

        private static Dictionary<string, object?> ToAnthropicArguments(KernelArguments arguments)
        {
            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var argument in arguments ?? new KernelArguments())
                result[argument.Key] = argument.Value;

            return result;
        }

        private static List<ContentBase> ToAnthropicTextContent(string content)
        {
            return new List<ContentBase>
            {
                new Anthropic.SDK.Messaging.TextContent
                {
                    Text = content?.Trim() ?? string.Empty
                }
            };
        }

        private static void ApplyCacheControl(IEnumerable<ContentBase> contentItems)
        {
            foreach (var contentItem in contentItems ?? Enumerable.Empty<ContentBase>())
                contentItem.CacheControl = new CacheControl { Type = CacheControlType.ephemeral };
        }

        private static JsonNode ToJsonNode(object value)
        {
            return JsonSerializer.SerializeToNode(value) ?? new JsonObject();
        }
    }
}
