using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;

namespace FlowBlox.AIAssistant.Services;

public partial class AiAssistantService
{
    private async Task<string> TryCompactContentAsync(
        string content,
        AssistantConfiguration config,
        string currentTask,
        int threshold,
        int targetTokens,
        CancellationToken ct,
        bool isToolResult = false,
        AIChatRequest? prefix = null)
    {
        var budget = BuildTokenBudget(config);
        var compacted = content;
        try
        {
            // Shared by summary and tool results: one attempt plus one corrective retry, never hard truncation.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var request = AssistantCompactionRequestBuilder.Build(
                    compacted,
                    currentTask,
                    budget.EstimateTokens(compacted),
                    threshold,
                    targetTokens,
                    Math.Max(1, config.ApproximateCharactersPerToken),
                    isToolResult);
                if (prefix != null)
                    request = ChooseMaintenanceRequest(AssistantMaintenanceRequestBuilder.Append(prefix, request), request, prefix, config);
                var response = await ExecuteChatWithCommunicationStatusAsync(
                    request,
                    config,
                    FlowBloxResourceUtil.GetLocalizedString("AiAssistantService_CompactingContext", typeof(FlowBloxTexts)),
                    ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (!response.Success || string.IsNullOrWhiteSpace(response.OutputText))
                {
                    _logger?.Warn($"AI Assistant context compaction failed: {response.Error}");
                    break;
                }

                if (prefix != null && request.SystemMessages.Count > 1)
                {
                    prefix = AssistantMaintenanceRequestBuilder.Continue(request, response.OutputText.Trim());
                }

                var candidate = response.OutputText.Trim();
                if (budget.EstimateTokens(candidate) < budget.EstimateTokens(compacted))
                    compacted = candidate;

                if (budget.EstimateTokens(compacted) <= targetTokens)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.Warn("AI Assistant context compaction failed.", ex);
        }

        return compacted;
    }

    private async Task TryCompactLatestToolResultsAsync(
        AssistantSessionState session,
        AssistantConfiguration config,
        string currentTask,
        string modelPrompt,
        CancellationToken ct)
    {
        var threshold = Math.Max(1, config.ToolResultCompactionThreshold);
        var ratio = Math.Clamp(config.SummaryCompactionTargetRatio, 0.1d, 0.9d);
        var targetTokens = Math.Max(1, (int)Math.Floor(threshold * ratio));
        var visited = new HashSet<AssistantMessagePair>();

        // A smaller projection may admit additional recent messages. Apply the same selection rules until stable.
        while (true)
        {
            var budget = BuildTokenBudget(config);
            AssistantMessagePair[] pairs;
            lock (_sessionSync)
            {
                var initial = session.InitialSystemMessages!;
                var selection = AssistantChatRequestBuilder.Build(
                    initial[0].Content,
                    initial[1].Content,
                    session.ConversationSummary,
                    session.Messages,
                    modelPrompt,
                    config.MaxLatestMessages,
                    config.MinLatestMessages,
                    config.SelectedProvider,
                    budget,
                    session.SummarizedMessageCount,
                    GetRuntimeSystemContents(session),
                    0);
                pairs = session.Messages
                    .Skip(selection.FirstIncludedHistoryMessageIndex)
                    .Take(selection.IncludedHistoryMessageCount)
                    .OfType<AssistantMessagePair>()
                    .Where(visited.Add)
                    .ToArray();
            }

            if (pairs.Length == 0)
                return;

            foreach (var pair in pairs)
            {
                var results = AssistantToolResultContext.GetOriginalResults(pair);
                var activeResults = AssistantToolResultContext.ParseResults(AssistantToolResultContext.GetResponse(pair));
                for (var index = 0; index < results.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    var activeResult = index < activeResults.Count
                        ? activeResults[index].ToString(Formatting.None)
                        : results[index];
                    if (AssistantToolResultContext.IsProtected(pair, index) || budget.EstimateTokens(activeResult) <= threshold)
                        continue;

                    var compacted = await TryCompactContentAsync(
                        activeResult,
                        config,
                        currentTask,
                        threshold,
                        targetTokens,
                        ct,
                        isToolResult: true).ConfigureAwait(false);
                    var projected = AssistantToolResultContext.WrapCompactedResult(compacted, results[index]).ToString(Formatting.None);
                    if (budget.EstimateTokens(projected) >= budget.EstimateTokens(activeResult))
                        continue;

                    lock (_sessionSync)
                        pair.CompactedToolResults[index] = compacted;

                    DebugStateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }
}
