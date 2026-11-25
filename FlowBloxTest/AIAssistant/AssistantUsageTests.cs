using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantUsageTests
{
    [TestMethod]
    public void OpenAIUsage_CountsCacheInsidePromptAndReasoningInsideCompletion()
    {
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":{"prompt_tokens":1000,"completion_tokens":200,
            "prompt_tokens_details":{"cached_tokens":800},"completion_tokens_details":{"reasoning_tokens":150}}}
            """), false);
        Assert.AreEqual(1000L, usage.TotalInputTokens);
        Assert.AreEqual(200L, usage.UncachedInputTokens);
        Assert.AreEqual(1200L, usage.TotalTokens);
    }

    [TestMethod]
    public void DeepSeekUsage_PreservesNativeCacheHitsAndMisses()
    {
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":{"prompt_tokens":1000,"completion_tokens":20,"prompt_cache_hit_tokens":900,"prompt_cache_miss_tokens":100}}
            """), false);
        Assert.AreEqual(900L, usage.CachedInputTokens);
        Assert.AreEqual(100L, usage.UncachedInputTokens);
        Assert.AreEqual(1020L, usage.TotalTokens);
    }

    [TestMethod]
    public void MoonshotUsage_AcceptsChoiceUsageAndNativeCachedTokens()
    {
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":null,"choices":[{"usage":{"prompt_tokens":1000,"completion_tokens":20,"cached_tokens":800}}]}
            """), false);
        Assert.AreEqual(1000L, usage.TotalInputTokens);
        Assert.AreEqual(800L, usage.CachedInputTokens);
        Assert.AreEqual(200L, usage.UncachedInputTokens);
    }

    [TestMethod]
    public void AnthropicUsage_AddsCacheReadsAndWritesToRegularInput()
    {
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":{"input_tokens":100,"cache_read_input_tokens":800,"cache_creation_input_tokens":100,"output_tokens":50}}
            """), true);
        Assert.AreEqual(1000L, usage.TotalInputTokens);
        Assert.AreEqual(100L, usage.UncachedInputTokens);
        Assert.AreEqual(1050L, usage.TotalTokens);
        var cost = AssistantCostCalculator.Calculate(usage, Pricing());
        Assert.IsNotNull(cost);
        Assert.AreEqual(0.405m, cost.Value.Cost);
        Assert.AreEqual(1.1m, cost.Value.WithoutCaching);
    }

    [TestMethod]
    public void MissingCacheMetadata_RemainsUnknownAndCannotProduceInventedCosts()
    {
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":{"prompt_tokens":100,"completion_tokens":50}}
            """), false);
        Assert.IsNull(usage.CachedInputTokens);
        Assert.IsNull(AssistantCostCalculator.Calculate(usage, Pricing()));
        Assert.IsNull(AIUsage.FromResponse(new JObject(), false).TotalInputTokens);
    }

    [TestMethod]
    public void SessionAggregation_FreezesRequestPricesAcrossModelSwitchAndPreservesUnknownCosts()
    {
        var session = new AssistantSessionUsage();
        var usage = AIUsage.FromResponse(JObject.Parse("""
            {"usage":{"prompt_tokens":1000,"completion_tokens":50,"prompt_tokens_details":{"cached_tokens":800}}}
            """), false);
        var firstPricing = Pricing();
        session.Add(usage, firstPricing);
        firstPricing.InputCostPerToken = 0.01m;
        session.Add(usage, firstPricing);
        Assert.AreEqual(2.56m, session.EstimatedCost);
        Assert.AreEqual(11.2m, session.CostWithoutCaching);
        session.Add(usage, null);
        Assert.AreEqual(2.56m, session.EstimatedCost);
        Assert.AreEqual(1, session.UnpricedRequests);
        Assert.AreEqual(3000L, session.TotalInputTokens);
    }

    [TestMethod]
    public void CacheWritePremium_CanProduceNegativeSavings()
    {
        var usage = new AIUsage
        {
            TotalInputTokens = 1000,
            UncachedInputTokens = 0,
            CachedInputTokens = 0,
            CacheWriteInputTokens = 1000,
            OutputTokens = 0
        };
        var session = new AssistantSessionUsage();
        session.Add(usage, Pricing());
        Assert.AreEqual(-0.25m, session.SavedByCaching);
        Assert.AreEqual(-0.25m, session.CostSavingRate);
    }

    [TestMethod]
    public void LongContextTier_UsesTotalInputIncludingCacheForAllRates()
    {
        var pricing = Pricing();
        pricing.Tiers.Add(new ModelPricingTier
        {
            AboveInputTokens = 200000,
            InputCostPerToken = 0.002m,
            OutputCostPerToken = 0.004m,
            CacheReadCostPerToken = 0.0002m
        });
        var usage = new AIUsage
        {
            TotalInputTokens = 210000,
            UncachedInputTokens = 10000,
            CachedInputTokens = 200000,
            CacheWriteInputTokens = 0,
            OutputTokens = 50
        };
        var cost = AssistantCostCalculator.Calculate(usage, pricing);
        Assert.IsNotNull(cost);
        Assert.AreEqual(60.2m, cost.Value.Cost);
        Assert.AreEqual(420.2m, cost.Value.WithoutCaching);
    }

    private static ModelPricing Pricing() => new()
    {
        InputCostPerToken = 0.001m,
        CacheReadCostPerToken = 0.0001m,
        CacheWriteCostPerToken = 0.00125m,
        OutputCostPerToken = 0.002m
    };
}
