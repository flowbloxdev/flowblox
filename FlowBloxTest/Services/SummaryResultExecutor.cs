using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBloxTest.Services;

internal sealed class SummaryResultExecutor(
    bool success,
    string output,
    params string[] responses) : IAiExecutor
{
    private readonly QueuedAiExecutor _conversation = new(responses);
    public Action? SummaryReceived { get; init; }

    public Task<AiExecutorResult> ExecuteChatAsync(
        AIChatRequest request,
        AssistantConfiguration configuration,
        CancellationToken ct)
    {
        if (request.Source != "FlowBloxAIAssistantSummary")
            return _conversation.ExecuteChatAsync(request, configuration, ct);

        SummaryReceived?.Invoke();
        return Task.FromResult(new AiExecutorResult { Success = success, OutputText = output });
    }
}
