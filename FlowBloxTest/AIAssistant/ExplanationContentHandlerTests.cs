using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
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
        public async Task LoadExplanation_ReturnsGuidanceInHistoryAndMarksItForDeferredRetentionWithoutRepeatingItsPayload()
        {
            using var retention = ToolHandlerUtilities.BeginRetainedContentScope(_ => null);
            var handler = new LoadExplanationHandler();

            var first = await handler.HandleAsync(
                new JObject { ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey },
                CancellationToken.None);
            var second = await handler.HandleAsync(
                new JObject { ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey },
                CancellationToken.None);

            Assert.IsTrue(first.Ok);
            Assert.AreEqual(true, first.Result.Value<bool>("loaded"));
            Assert.AreEqual(AssistantPromptCatalog.GetPromptContentOrNull(AssistantPromptCatalog.SpecialGeneratorsKey),
                first.Result.Value<string>("content"));
            Assert.AreEqual(false, second.Result.Value<bool>("loaded"));
            Assert.IsNull(second.Result["content"]);
            var marked = first.RetainedContents.Single();
            Assert.AreEqual(AssistantSystemMessageKind.Explanation, marked.Kind);
            Assert.AreEqual(AssistantPromptCatalog.GetPromptContentOrNull(AssistantPromptCatalog.SpecialGeneratorsKey), marked.Content);
            Assert.AreEqual(marked.Key, second.RetainedContents.Single().Key);
            Assert.AreEqual(string.Empty, second.RetainedContents.Single().Content);
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
            Assert.AreEqual(0, response.RetainedContents.Count);
        }

        [TestMethod]
        public void BuildChatRequest_AddsLoadedExplanationAsSystemMessage()
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
        }
    }
}
