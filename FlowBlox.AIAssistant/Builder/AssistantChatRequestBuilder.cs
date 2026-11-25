using FlowBlox.AIAssistant.Constants;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Services;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Builder
{
    internal static class AssistantChatRequestBuilder
    {
        private const string ToolPluginName = "FlowBloxAIToolApi";
        private static readonly IAiResponseInstructionParserService InstructionParserService =
            FlowBloxServiceLocator.Instance.GetService<IAiResponseInstructionParserService>();

        public static AssistantChatRequestBuildResult Build(
            string systemPrompt,
            string sessionBootstrapPrompt,
            string conversationSummary,
            IReadOnlyList<AssistantSessionMessage> sessionMessages,
            string modelPrompt,
            int maxLatestMessages,
            int minLatestMessages,
            AIProviderBase? provider,
            AssistantTokenBudget tokenBudget,
            int summarizedMessageCount = 0,
            IReadOnlyList<string>? runtimeSystemContents = null,
            int preloadedRuntimeSystemMessageCount = 0)
        {
            var request = new AIChatRequest();
            request.SystemMessages.Add(new AIChatMessage
            {
                Role = "system",
                Content = systemPrompt,
                CacheBehavior = AIChatCacheBehavior.PreferCache
            });
            request.SystemMessages.Add(new AIChatMessage
            {
                Role = "system",
                Content = sessionBootstrapPrompt,
                CacheBehavior = AIChatCacheBehavior.PreferCache
            });
            var bootstrapBoundary = request.SystemMessages[^1];
            var runtimeMessageIndex = 0;
            foreach (var content in runtimeSystemContents ?? [])
            {
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var runtimeMessage = new AIChatMessage
                {
                    Role = "system",
                    Content = content.Trim(),
                    CacheBehavior = AIChatCacheBehavior.PreferCache
                };
                request.SystemMessages.Add(runtimeMessage);
                if (runtimeMessageIndex < preloadedRuntimeSystemMessageCount)
                    bootstrapBoundary = runtimeMessage;

                runtimeMessageIndex++;
            }

            if (provider is AnthropicAIProvider)
                bootstrapBoundary.IsAnthropicBootstrapCacheBoundary = true;

            var summaryMessageForBudget = default(AIChatMessage);
            if (!string.IsNullOrWhiteSpace(conversationSummary))
            {
                var summaryContent = "Conversation Summary:\n" + conversationSummary.Trim();
                summaryMessageForBudget = new AIChatMessage
                {
                    Role = "summary",
                    Content = summaryContent,
                    CacheBehavior = AIChatCacheBehavior.PreferCache,
                    SemanticContent = new ChatMessageContent(AuthorRole.User, summaryContent)
                };
                request.Messages.Add(summaryMessageForBudget);
            }

            var fixedMessagesForBudget = request.SystemMessages.ToList();
            if (summaryMessageForBudget != null)
                fixedMessagesForBudget.Add(summaryMessageForBudget);

            var historyStartIndex = Math.Clamp(summarizedMessageCount, 0, sessionMessages?.Count ?? 0);
            var messagesAvailableForHistory = sessionMessages?
                .Skip(historyStartIndex)
                .ToList() ?? new List<AssistantSessionMessage>();

            var remainingHistoryTokens = CalculateRemainingHistoryTokens(
                fixedMessagesForBudget,
                modelPrompt,
                tokenBudget);
            var latestSelection = SelectLatestMessages(
                messagesAvailableForHistory,
                maxLatestMessages,
                minLatestMessages,
                remainingHistoryTokens,
                tokenBudget,
                historyStartIndex);

            for (var i = 0; i < latestSelection.Messages.Count; i++)
                AddSessionMessage(
                    request.Messages,
                    latestSelection.Messages[i],
                    latestSelection.FirstIncludedHistoryMessageIndex + i,
                    provider);

            request.Messages.Add(new AIChatMessage
            {
                Role = "user",
                Content = modelPrompt
            });

            return new AssistantChatRequestBuildResult
            {
                Request = request,
                FirstIncludedHistoryMessageIndex = latestSelection.FirstIncludedHistoryMessageIndex,
                IncludedHistoryMessageCount = latestSelection.Messages.Count
            };
        }

        private static void AddSessionMessage(
            List<AIChatMessage> requestMessages,
            AssistantSessionMessage message,
            int sessionMessageIndex,
            AIProviderBase? provider)
        {
            if (message is AssistantMessagePair pair)
            {
                AddMessagePair(requestMessages, pair, sessionMessageIndex, provider);
                return;
            }

            requestMessages.Add(new AIChatMessage
            {
                Role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    ? "assistant"
                    : "user",
                Content = message.CompleteMessage.Trim()
            });
        }

        private static void AddMessagePair(
            List<AIChatMessage> requestMessages,
            AssistantMessagePair pair,
            int sessionMessageIndex,
            AIProviderBase? provider)
        {
            var assistantRequest = pair.AssistantRequest?.Trim() ?? string.Empty;
            var toolApiResponse = pair.ToolApiResponse?.Trim() ?? string.Empty;
            var assistantRequestContent = "Assistant request:\n" + assistantRequest;
            var toolApiResponseContent = "Tool API response:\n" + toolApiResponse;
            if (provider?.SupportsNativeFunctionCallHistory != true)
            {
                requestMessages.Add(new AIChatMessage
                {
                    Role = "assistant",
                    Content = assistantRequestContent
                });
                requestMessages.Add(new AIChatMessage
                {
                    Role = "tool",
                    Content = toolApiResponseContent
                });
                return;
            }

            var functionCalls = BuildFunctionCalls(assistantRequest, sessionMessageIndex);

            if (functionCalls.Count == 0)
            {
                requestMessages.Add(new AIChatMessage
                {
                    Role = "assistant",
                    Content = assistantRequestContent
                });
                requestMessages.Add(new AIChatMessage
                {
                    Role = "tool",
                    Content = toolApiResponseContent
                });
                return;
            }

            var assistantContent = new ChatMessageContent(
                AuthorRole.Assistant,
                new ChatMessageContentItemCollection(),
                metadata: provider?.BuildChatMessageMetadata(pair.Metadata));
            foreach (var functionCall in functionCalls)
                assistantContent.Items.Add(functionCall);

            requestMessages.Add(new AIChatMessage
            {
                Role = "assistant",
                Content = assistantRequestContent,
                SemanticContent = assistantContent
            });

            var toolContent = new ChatMessageContent(AuthorRole.Tool, new ChatMessageContentItemCollection());
            foreach (var functionResult in BuildFunctionResults(functionCalls, toolApiResponse))
                toolContent.Items.Add(functionResult);

            requestMessages.Add(new AIChatMessage
            {
                Role = "tool",
                Content = toolApiResponseContent,
                SemanticContent = toolContent
            });
        }

        private static List<FunctionCallContent> BuildFunctionCalls(
            string assistantRequest,
            int sessionMessageIndex)
        {
            var parseResult = InstructionParserService.Parse(assistantRequest);
            var toolCalls = parseResult.Instruction?.ToolCalls ?? new List<AiResponseToolCall>();
            var functionCalls = new List<FunctionCallContent>();

            for (var i = 0; i < toolCalls.Count; i++)
            {
                var toolCall = toolCalls[i];
                if (string.IsNullOrWhiteSpace(toolCall.ToolName))
                    continue;

                functionCalls.Add(new FunctionCallContent(
                    toolCall.ToolName,
                    ToolPluginName,
                    BuildToolCallId(sessionMessageIndex, i),
                    ToKernelArguments(toolCall.Arguments)));
            }

            return functionCalls;
        }

        private static List<FunctionResultContent> BuildFunctionResults(
            IReadOnlyList<FunctionCallContent> functionCalls,
            string toolApiResponse)
        {
            var transcriptItems = ParseToolTranscriptItems(toolApiResponse);
            var functionResults = new List<FunctionResultContent>();

            for (var i = 0; i < functionCalls.Count; i++)
            {
                var result = i < transcriptItems.Count
                    ? transcriptItems[i].ToString(Formatting.None)
                    : toolApiResponse;

                functionResults.Add(new FunctionResultContent(functionCalls[i], result));
            }

            return functionResults;
        }

        private static List<JObject> ParseToolTranscriptItems(string toolApiResponse)
        {
            var items = new List<JObject>();
            if (string.IsNullOrWhiteSpace(toolApiResponse))
                return items;

            using var reader = new StringReader(toolApiResponse);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("{", StringComparison.Ordinal))
                    continue;

                try
                {
                    var obj = JObject.Parse(trimmed);
                    if (obj["response"] is JToken response)
                        items.Add(response as JObject ?? new JObject { ["value"] = response });
                    else
                        items.Add(obj);
                }
                catch (JsonException ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        "Failed to parse a JSON line from a persisted Tool API response while rebuilding AI Assistant chat history.",
                        ex);
                }
            }

            return items;
        }

        private static KernelArguments ToKernelArguments(JObject arguments)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var property in arguments?.Properties() ?? Enumerable.Empty<JProperty>())
                values[property.Name] = ToPlainValue(property.Value);

            return new KernelArguments(values);
        }

        private static object? ToPlainValue(JToken token)
        {
            return token switch
            {
                JObject obj => obj.Properties().ToDictionary(
                    property => property.Name,
                    property => ToPlainValue(property.Value),
                    StringComparer.Ordinal),
                JArray array => array.Select(ToPlainValue).ToList(),
                JValue value => value.Value,
                _ => token.ToString(Formatting.None)
            };
        }

        private static string BuildToolCallId(int sessionMessageIndex, int toolCallIndex)
        {
            return $"flowblox_tool_call_{sessionMessageIndex}_{toolCallIndex}";
        }

        private static int CalculateRemainingHistoryTokens(
            IReadOnlyList<AIChatMessage> systemMessages,
            string modelPrompt,
            AssistantTokenBudget tokenBudget)
        {
            ArgumentNullException.ThrowIfNull(tokenBudget);

            var maxContextTokens = Math.Max(AssistantConfigurationLimits.MinContextTokens, tokenBudget.MaxContextTokens);
            if (maxContextTokens == 0)
                return int.MaxValue;

            var reservedResponseTokens = Math.Max(AssistantConfigurationLimits.MinReservedResponseTokens, tokenBudget.ReservedResponseTokens);
            var fixedTokens = tokenBudget.EstimateTokens(modelPrompt);
            foreach (var systemMessage in systemMessages ?? Array.Empty<AIChatMessage>())
                fixedTokens += tokenBudget.EstimateTokens(systemMessage?.Content);

            return Math.Max(0, maxContextTokens - reservedResponseTokens - fixedTokens);
        }

        private static LatestMessageSelection SelectLatestMessages(
            IReadOnlyList<AssistantSessionMessage> sessionMessages,
            int maxLatestMessages,
            int minLatestMessages,
            int maxHistoryTokens,
            AssistantTokenBudget tokenBudget,
            int firstSessionMessageIndex)
        {
            ArgumentNullException.ThrowIfNull(tokenBudget);

            var messages = sessionMessages ?? Array.Empty<AssistantSessionMessage>();
            var maxMessages = Math.Clamp(
                maxLatestMessages,
                AssistantConfigurationLimits.MinLatestMessages,
                AssistantConfigurationLimits.MaxLatestMessages);
            var minMessages = Math.Clamp(
                minLatestMessages,
                AssistantConfigurationLimits.MinLatestMessages,
                maxMessages);

            if (maxMessages == 0)
            {
                return new LatestMessageSelection
                {
                    FirstIncludedHistoryMessageIndex = firstSessionMessageIndex + messages.Count
                };
            }

            var indexedCandidates = messages
                .Select((message, index) => new IndexedConversationMessage(message, firstSessionMessageIndex + index))
                .Where(x => !string.IsNullOrWhiteSpace(x.Message?.CompleteMessage))
                .Reverse()
                .ToList();

            var selected = new List<IndexedConversationMessage>();
            var selectedMessageCount = 0;
            var usedTokens = 0;

            foreach (var candidate in indexedCandidates)
            {
                if (selectedMessageCount > 0 && selectedMessageCount + 1 > maxMessages)
                    break;

                var messageTokens = tokenBudget.EstimateTokens(candidate.Message.CompleteMessage);
                if (selectedMessageCount >= minMessages && usedTokens + messageTokens > maxHistoryTokens)
                    break;

                selected.Add(candidate);
                selectedMessageCount++;
                usedTokens += messageTokens;
            }

            selected.Reverse();

            return new LatestMessageSelection
            {
                Messages = selected.Select(x => x.Message).ToList(),
                FirstIncludedHistoryMessageIndex = selected.Count == 0
                    ? messages.Count
                    : selected.Min(x => x.Index)
            };
        }

        private sealed class LatestMessageSelection
        {
            public List<AssistantSessionMessage> Messages { get; init; } = new();
            public int FirstIncludedHistoryMessageIndex { get; init; }
        }

        private sealed class IndexedConversationMessage
        {
            public IndexedConversationMessage(AssistantSessionMessage message, int index)
            {
                Message = message;
                Index = index;
            }

            public AssistantSessionMessage Message { get; }
            public int Index { get; }
        }
    }
}
