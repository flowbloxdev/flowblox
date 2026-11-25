using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBlox.AIAssistant.Models;

/// <summary>
/// Cumulative measured session usage and request-time priced totals. Missing data remains explicit.
/// </summary>
public sealed class AssistantSessionUsage
{
    public long TotalInputTokens { get; set; }
    public long CachedInputTokens { get; set; }
    public long UncachedInputTokens { get; set; }
    public long CacheWriteInputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => TotalInputTokens + OutputTokens;
    public int Requests { get; set; }
    public int UnpricedRequests { get; set; }
    public int IncompleteUsageRequests { get; set; }
    public decimal EstimatedCost { get; set; }
    public decimal CostWithoutCaching { get; set; }
    public decimal SavedByCaching => CostWithoutCaching - EstimatedCost;
    public decimal? CostSavingRate => CostWithoutCaching > 0 ? SavedByCaching / CostWithoutCaching : null;
    public double? CacheHitRate => TotalInputTokens > 0 ? (double)CachedInputTokens / TotalInputTokens : null;

    public AssistantSessionUsage Clone() => (AssistantSessionUsage)MemberwiseClone();

    public void Add(AIUsage usage, ModelPricing? pricing)
    {
        Requests++;
        TotalInputTokens += usage.TotalInputTokens ?? 0;
        CachedInputTokens += usage.CachedInputTokens ?? 0;
        UncachedInputTokens += usage.UncachedInputTokens ?? 0;
        CacheWriteInputTokens += usage.CacheWriteInputTokens ?? 0;
        OutputTokens += usage.OutputTokens ?? 0;
        if (!usage.TotalInputTokens.HasValue || !usage.OutputTokens.HasValue ||
            !usage.UncachedInputTokens.HasValue || !usage.CachedInputTokens.HasValue || !usage.CacheWriteInputTokens.HasValue)
            IncompleteUsageRequests++;

        var cost = Services.AssistantCostCalculator.Calculate(usage, pricing);
        if (cost == null)
        {
            UnpricedRequests++;
            return;
        }
        EstimatedCost += cost.Value.Cost;
        CostWithoutCaching += cost.Value.WithoutCaching;
    }
}
