using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace FlowBloxTest.Services
{
    internal sealed class RecordingOpenAICompatibleProvider : OpenAICompatibleProviderBase
    {
        private readonly Queue<string> _normalResponses;

        public RecordingOpenAICompatibleProvider()
            : this([])
        {
        }

        public RecordingOpenAICompatibleProvider(params string[] normalResponses)
            : base("https://api.openai.com/v1", "test-model")
        {
            ApiKey = "test-api-key";
            TimeoutSeconds = 30;
            _normalResponses = new Queue<string>(normalResponses ?? Array.Empty<string>());
        }

        public override string ProviderType => "TestOpenAICompatible";
        protected override string ProviderDisplayName => "Test OpenAI Compatible";
        public int SummaryRequestCount { get; private set; }
        public List<IReadOnlyList<ChatMessageContent>> NormalChatHistories { get; } = new();
        public List<IReadOnlyList<ChatMessageContent>> SummaryChatHistories { get; } = new();
        public List<OpenAIPromptExecutionSettings> ExecutionSettings { get; } = new();

        protected override Task<ChatMessageContent> SendChatMessageContentAsync(
            IChatCompletionService chatService,
            AIChatRequest request,
            ChatHistory chatHistory,
            OpenAIPromptExecutionSettings executionSettings,
            CancellationToken ct)
        {
            ExecutionSettings.Add(executionSettings);
            var snapshot = chatHistory.ToList();
            if (request.Source == "FlowBloxAIAssistantSummary")
            {
                SummaryRequestCount++;
                SummaryChatHistories.Add(snapshot);
                return Task.FromResult(new ChatMessageContent(AuthorRole.Assistant, $"SUMMARY-{SummaryRequestCount}"));
            }

            if (request.Source == "FlowBloxAIAssistantCompaction")
                return Task.FromResult(new ChatMessageContent(AuthorRole.Assistant, "COMPACTED"));

            NormalChatHistories.Add(snapshot);
            return Task.FromResult(new ChatMessageContent(
                AuthorRole.Assistant,
                _normalResponses.Count == 0
                    ? "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}"
                    : _normalResponses.Dequeue()));
        }
    }
}
