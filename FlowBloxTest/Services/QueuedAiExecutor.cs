using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBloxTest.Services
{
    internal sealed class QueuedAiExecutor : IAiExecutor
    {
        private readonly Queue<string> _responses;

        public QueuedAiExecutor(params string[] responses)
        {
            _responses = new Queue<string>(responses ?? Array.Empty<string>());
        }

        public List<AIChatRequest> Requests { get; } = new();
        public List<AIChatRequest> ChatRequests => Requests
            .Where(x => x.Source != "FlowBloxAIAssistantSummary")
            .ToList();

        public Task<AiExecutorResult> ExecuteChatAsync(
            AIChatRequest request,
            AssistantConfiguration configuration,
            CancellationToken ct)
        {
            Requests.Add(request);

            if (request.Source == "FlowBloxAIAssistantSummary")
            {
                return Task.FromResult(new AiExecutorResult
                {
                    Success = true,
                    OutputText = "[SUMMARIZED]\n" + request.GetInputText()
                });
            }

            return Task.FromResult(new AiExecutorResult
            {
                Success = true,
                OutputText = _responses.Count == 0
                    ? "{\"assistantMessage\":\"Done.\",\"final\":true,\"toolCalls\":[]}"
                    : _responses.Dequeue()
            });
        }
    }
}