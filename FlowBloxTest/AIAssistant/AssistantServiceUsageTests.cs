using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantServiceUsageTests
{
    [TestMethod]
    public async Task Usage_IncludesSummariesOnceAndResetPreservesHistoryAndContext()
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var configuration = new AssistantConfiguration
        {
            MaxLatestMessages = 2,
            MinLatestMessages = 2,
            SummaryCompactionRate = 1d,
            EnableAutomaticAdjustment = false
        };
        configuration.Providers.Add(new OpenAIProvider { Name = "Fixture", DefaultModel = "synthetic" });
        var executor = new UsageExecutor();
        var service = new AiAssistantService(executor, new DefaultToolApi(),
            configurationProvider: () => configuration, pricingCatalogue: new FixturePricing());
        try
        {
            await service.ExecutePromptAsync("First", CancellationToken.None);
            await service.ExecutePromptAsync("Second", CancellationToken.None);
            await service.ExecutePromptAsync("Third", CancellationToken.None);
            Assert.IsTrue(executor.SummaryRequests > 0);
            Assert.AreEqual(executor.Requests, service.SessionUsage.Requests);
            Assert.AreEqual(executor.Requests * 1000L, service.SessionUsage.TotalInputTokens);
            Assert.AreEqual(executor.Requests * 1.1m, service.SessionUsage.EstimatedCost);
            var context = service.CurrentContextTokens;
            var before = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(before);
            service.ResetSessionUsage();
            var after = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(after);
            Assert.AreEqual(0, after.Usage.Requests);
            Assert.AreEqual(before.SessionMessages.Count, after.SessionMessages.Count);
            Assert.AreEqual(before.ConversationSummary, after.ConversationSummary);
            Assert.AreEqual(context, service.CurrentContextTokens);
        }
        finally
        {
            service.ResetSession();
        }
    }

    private sealed class FixturePricing : IModelPricingCatalogueService
    {
        public Task<ModelPricing?> GetPricingAsync(string provider, string model, CancellationToken cancellationToken)
            => Task.FromResult<ModelPricing?>(new ModelPricing
            {
                InputCostPerToken = 0.001m,
                OutputCostPerToken = 0.002m
            });
    }

    private sealed class UsageExecutor : IAiExecutor
    {
        public int Requests { get; private set; }
        public int SummaryRequests { get; private set; }

        public Task<AiExecutorResult> ExecuteChatAsync(AIChatRequest request, AssistantConfiguration configuration, CancellationToken ct)
        {
            Requests++;
            var summary = request.Source == "FlowBloxAIAssistantSummary";
            if (summary)
                SummaryRequests++;
            return Task.FromResult(new AiExecutorResult
            {
                Success = true,
                OutputText = summary ? "Saved memory" : """{"assistantMessage":"Done","final":true,"toolCalls":[]}""",
                Usage = new AIUsage
                {
                    TotalInputTokens = 1000,
                    UncachedInputTokens = 1000,
                    CachedInputTokens = 0,
                    CacheWriteInputTokens = 0,
                    OutputTokens = 50
                }
            });
        }
    }
}
