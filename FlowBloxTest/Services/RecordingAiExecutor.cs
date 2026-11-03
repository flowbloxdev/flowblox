using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBloxTest.Services
{
    internal sealed class RecordingAiExecutor : IAiExecutor
    {
        public List<AIChatRequest> Requests { get; } = new();
        public Action<AIChatRequest>? SummaryRequestReceived { get; set; }
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
                SummaryRequestReceived?.Invoke(request);
                return Task.FromResult(new AiExecutorResult
                {
                    Success = true,
                    OutputText = "[SUMMARIZED]\n" + request.Messages.Single().Content
                });
            }

            var userPrompt = ExtractUserPrompt(request.Messages.Last().Content);
            return Task.FromResult(new AiExecutorResult
            {
                Success = true,
                OutputText = "{\"assistantMessage\":\"ASSISTANT-" + userPrompt + "\",\"final\":true,\"toolCalls\":[]}"
            });
        }

        private static string ExtractUserPrompt(string roundPrompt)
        {
            var marker = "User prompt:";
            var markerIndex = roundPrompt.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return roundPrompt.Trim();

            var afterMarker = roundPrompt[(markerIndex + marker.Length)..].TrimStart();
            using var reader = new StringReader(afterMarker);
            return reader.ReadLine()?.Trim() ?? string.Empty;
        }
    }
}
