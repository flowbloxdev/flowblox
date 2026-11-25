using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBloxTest.Services;

internal sealed class ToolResultCompactionExecutor : IAiExecutor
{
    public List<AIChatRequest> Requests { get; } = new();
    public Func<AIChatRequest, AiExecutorResult>? SummaryResponse { get; set; }
    public Func<AIChatRequest, AiExecutorResult>? CompactionResponse { get; set; }
    public List<AIChatRequest> ToolCompactions => Requests
        .Where(request => request.Meta.GetValueOrDefault("CompactionKind")?.ToString() == "ToolResult")
        .ToList();
    public List<AIChatRequest> ChatRequests => Requests
        .Where(request => request.Source != "FlowBloxAIAssistantSummary" && request.Source != "FlowBloxAIAssistantCompaction")
        .ToList();

    public Task<AiExecutorResult> ExecuteChatAsync(
        AIChatRequest request,
        AssistantConfiguration configuration,
        CancellationToken ct)
    {
        Requests.Add(request);
        if (request.Source == "FlowBloxAIAssistantSummary")
            return Task.FromResult(SummaryResponse?.Invoke(request) ?? new AiExecutorResult { Success = true, OutputText = "Memory" });

        if (request.Source == "FlowBloxAIAssistantCompaction")
            return Task.FromResult(CompactionResponse?.Invoke(request) ?? new AiExecutorResult
            {
                Success = true,
                OutputText = "Failed E42 at worker.cs:17; run=fixture-run"
            });

        return Task.FromResult(new AiExecutorResult
        {
            Success = true,
            OutputText = """{"assistantMessage":"Done","final":true,"toolCalls":[]}"""
        });
    }
}
