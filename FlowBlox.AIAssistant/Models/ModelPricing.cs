namespace FlowBlox.AIAssistant.Models;

public sealed class ModelPricing
{
    public decimal? InputCostPerToken { get; set; }
    public decimal? OutputCostPerToken { get; set; }
    public decimal? CacheReadCostPerToken { get; set; }
    public decimal? CacheWriteCostPerToken { get; set; }
    public int? MaxInputTokens { get; set; }
    public int? MaxOutputTokens { get; set; }
    public int? MaxTokens { get; set; }
    public List<ModelPricingTier> Tiers { get; set; } = new();
    public bool HasUnsupportedTiers { get; set; }
}
