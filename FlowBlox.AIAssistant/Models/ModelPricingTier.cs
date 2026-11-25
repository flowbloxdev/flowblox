namespace FlowBlox.AIAssistant.Models;

public sealed class ModelPricingTier
{
    public long AboveInputTokens { get; set; }
    public decimal? InputCostPerToken { get; set; }
    public decimal? OutputCostPerToken { get; set; }
    public decimal? CacheReadCostPerToken { get; set; }
    public decimal? CacheWriteCostPerToken { get; set; }
}
