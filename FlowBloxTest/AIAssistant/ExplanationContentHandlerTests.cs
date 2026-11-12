using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.State;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [DoNotParallelize]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class ExplanationContentHandlerTests
    {
        private readonly string _sessionId = Guid.NewGuid().ToString("N");

        [TestInitialize]
        public void Initialize()
        {
            ToolHandlerUtilities.SetCurrentSessionGuid(_sessionId);
        }

        [TestCleanup]
        public void Cleanup()
        {
            ToolHandlerUtilities.ClearSessionCache(_sessionId);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
        }

        [TestMethod]
        public async Task LoadExplanation_PromotesOnDemandContentImmediatelyAndOnlyOnce()
        {
            var handler = new LoadExplanationHandler();

            var first = await handler.HandleAsync(
                new JObject { ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey },
                CancellationToken.None);
            var second = await handler.HandleAsync(
                new JObject { ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey },
                CancellationToken.None);

            Assert.IsTrue(first.Ok);
            Assert.AreEqual(true, first.Result.Value<bool>("loaded"));
            Assert.IsNull(first.Result["content"]);
            Assert.AreEqual(false, second.Result.Value<bool>("loaded"));
            Assert.IsNull(second.Result["content"]);
            Assert.AreEqual(1, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
            CollectionAssert.AreEqual(
                new[] { AssistantPromptCatalog.SpecialGeneratorsKey },
                ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Select(x => x.Key.Replace("explanation:", "")).ToArray());
        }

        [TestMethod]
        public async Task LoadExplanation_DoesNotDuplicateInitiallyLoadedContent()
        {
            var response = await new LoadExplanationHandler().HandleAsync(
                new JObject { ["key"] = AssistantPromptCatalog.DebuggingKey },
                CancellationToken.None);

            Assert.IsTrue(response.Ok);
            Assert.AreEqual(false, response.Result.Value<bool>("loaded"));
            Assert.IsNull(response.Result["content"]);
            Assert.AreEqual(0, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
        }

        [TestMethod]
        public void BuildChatRequest_AddsLoadedExplanationAsCacheableSystemMessage()
        {
            var explanation = AssistantPromptCatalog.GetPromptContentOrNull(
                AssistantPromptCatalog.SpecialGeneratorsKey)!;

            var result = AssistantChatRequestBuilder.Build(
                systemPrompt: "SYSTEM",
                sessionBootstrapPrompt: "BOOTSTRAP",
                conversationSummary: string.Empty,
                sessionMessages: [],
                modelPrompt: "CONTINUE",
                maxLatestMessages: 5,
                minLatestMessages: 1,
                provider: null,
                tokenBudget: new AssistantTokenBudget
                {
                    MaxContextTokens = 100000,
                    ReservedResponseTokens = 0,
                    ApproximateCharactersPerToken = 4
                },
                runtimeSystemContents: [explanation]);

            Assert.AreEqual(3, result.Request.SystemMessages.Count);
            Assert.AreEqual(explanation, result.Request.SystemMessages[2].Content);
            Assert.AreEqual(
                FlowBlox.Core.Models.FlowBlocks.AIRemote.Base.AIChatCacheBehavior.PreferCache,
                result.Request.SystemMessages[2].CacheBehavior);
        }
    }
}
