using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBlox.AIAssistant.Services;

public static class AssistantCostCalculator
{
    public static (decimal Cost, decimal WithoutCaching)? Calculate(AIUsage usage, ModelPricing? pricing)
    {
        if (pricing == null || pricing.HasUnsupportedTiers || !pricing.InputCostPerToken.HasValue ||
            !pricing.OutputCostPerToken.HasValue || !usage.TotalInputTokens.HasValue || !usage.OutputTokens.HasValue ||
            !usage.UncachedInputTokens.HasValue || !usage.CachedInputTokens.HasValue || !usage.CacheWriteInputTokens.HasValue)
            return null;

        if (usage.TotalInputTokens < 0 || usage.OutputTokens < 0 || usage.UncachedInputTokens < 0 ||
            usage.CachedInputTokens < 0 || usage.CacheWriteInputTokens < 0 ||
            usage.TotalInputTokens != usage.UncachedInputTokens + usage.CachedInputTokens + usage.CacheWriteInputTokens)
            return null;

        var tier = pricing.Tiers.Where(x => usage.TotalInputTokens > x.AboveInputTokens)
            .OrderByDescending(x => x.AboveInputTokens)
            .FirstOrDefault();
        var regularPrice = tier?.InputCostPerToken ?? pricing.InputCostPerToken.Value;
        var outputPrice = tier?.OutputCostPerToken ?? pricing.OutputCostPerToken.Value;
        var readPrice = tier?.CacheReadCostPerToken ?? pricing.CacheReadCostPerToken;
        var writePrice = tier?.CacheWriteCostPerToken ?? pricing.CacheWriteCostPerToken;
        if (usage.CachedInputTokens > 0 && !readPrice.HasValue ||
            usage.CacheWriteInputTokens > 0 && !writePrice.HasValue)
            return null;

        if (regularPrice < 0 || outputPrice < 0 || readPrice < 0 || writePrice < 0)
            return null;

        var input = usage.UncachedInputTokens.Value * regularPrice;
        var read = usage.CachedInputTokens.Value * (readPrice ?? 0m);
        var write = usage.CacheWriteInputTokens.Value * (writePrice ?? 0m);
        var output = usage.OutputTokens.Value * outputPrice;
        return (input + read + write + output, usage.TotalInputTokens.Value * regularPrice + output);
    }
}
