using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBlox.AIAssistant.Services
{
    public class AiExecutorResult
    {
        public bool Success { get; set; }
        public string OutputText { get; set; }
        public string Error { get; set; }
        public string RawOutput { get; set; }
        public string ResponseId { get; set; }
        public AIChatMessageMetadata MessageMetadata { get; set; } = new();
        public AIUsage Usage { get; set; } = new();
        public int? PromptTokens { get; set; }
        public int? CompletionTokens { get; set; }
    }

    public interface IAiExecutor
    {
        Task<AiExecutorResult> ExecuteChatAsync(
            AIChatRequest request,
            AssistantConfiguration configuration,
            CancellationToken ct);
    }
}
