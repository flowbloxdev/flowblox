using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json.Linq;
using FlowBlox.AIAssistant.Builder;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AnthropicCacheRequestTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(20)]
    public void SerializedRequest_SeparatesBootstrapAndDynamicExplanations(int dynamicExplanationCount)
    {
        var explanations = new[] { "Preloaded explanation" }
            .Concat(Enumerable.Range(0, dynamicExplanationCount).Select(index => "Dynamic explanation " + index))
            .ToArray();
        var request = BuildAssistantRequest(explanations, 1);
        var body = Serialize(request);
        var system = (JArray)body["system"]!;
        var markedIndices = system.Select((block, index) => new { Block = block, Index = index })
            .Where(entry => entry.Block["cache_control"]?.Type == JTokenType.Object)
            .Select(entry => entry.Index)
            .ToArray();

        CollectionAssert.AreEqual(
            dynamicExplanationCount == 0 ? new[] { 2 } : new[] { 2, 2 + dynamicExplanationCount },
            markedIndices);
        var allMarkers = body.Descendants().OfType<JProperty>()
            .Count(property => property.Name == "cache_control" && property.Value.Type == JTokenType.Object);
        Assert.AreEqual(dynamicExplanationCount == 0 ? 2 : 3, allMarkers);
        Assert.IsTrue(allMarkers <= 4);
        Assert.AreEqual("ephemeral", body["messages"]!.Last!["content"]!.Last!["cache_control"]!["type"]!.Value<string>());
        Assert.AreEqual(
            JTokenType.Null,
            body["messages"]![0]!["content"]![0]!["cache_control"]?.Type ?? JTokenType.Null);
    }

    [TestMethod]
    public void SerializedRequest_AppendingExplanationsPreservesBootstrapPrefix()
    {
        var first = Serialize(BuildAssistantRequest(["Preloaded", "First dynamic"], 1));
        var next = Serialize(BuildAssistantRequest(["Preloaded", "First dynamic", "Next dynamic"], 1));
        var firstPrefix = new JArray(first["system"]!.Take(3));
        var nextPrefix = new JArray(next["system"]!.Take(3));
        Assert.IsTrue(JToken.DeepEquals(firstPrefix, nextPrefix));
        Assert.IsNotNull(next["system"]![2]!["cache_control"]);
        Assert.AreEqual(JTokenType.Null, next["system"]![3]!["cache_control"]?.Type ?? JTokenType.Null);
        Assert.IsNotNull(next["system"]![4]!["cache_control"]);
    }

    [TestMethod]
    public void SerializedRequest_WithoutSeparatePreloadsPinsBootstrapBeforeFirstDynamicExplanation()
    {
        var body = Serialize(BuildAssistantRequest(["Dynamic explanation"], 0));
        Assert.IsNotNull(body["system"]![1]!["cache_control"]);
        Assert.IsNotNull(body["system"]![2]!["cache_control"]);
    }

    private static AIChatRequest BuildAssistantRequest(string[] explanations, int preloadedCount)
    {
        return AssistantChatRequestBuilder.Build(
            "System instructions",
            new string('b', 24000),
            "Changing summary",
            [],
            "Current user message",
            20,
            1,
            new AnthropicAIProvider(),
            new AssistantTokenBudget
            {
                MaxContextTokens = 48000,
                ReservedResponseTokens = 4096,
                ApproximateCharactersPerToken = 5
            },
            runtimeSystemContents: explanations,
            preloadedRuntimeSystemMessageCount: preloadedCount).Request;
    }

    private static JObject Serialize(AIChatRequest request)
    {
        var parameters = AnthropicAIProvider.BuildMessageParameters(request, "claude-fixture");
        return JObject.Parse(System.Text.Json.JsonSerializer.Serialize(parameters));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    public void SerializedRequest_PinsLastStableSystemBlockAndLastConversationBlock(int kind)
    {
        var request = new AIChatRequest();
        request.SystemMessages.Add(new AIChatMessage { Content = "Bootstrap", CacheBehavior = AIChatCacheBehavior.PreferCache });
        request.SystemMessages.Add(new AIChatMessage { Content = "Explanation", CacheBehavior = AIChatCacheBehavior.PreferCache });
        request.Messages.Add(new AIChatMessage { Role = "user", Content = "Summary" });
        var semantic = new ChatMessageContent(AuthorRole.Tool, new ChatMessageContentItemCollection());
        if (kind == 2)
        {
            semantic = new ChatMessageContent(AuthorRole.Assistant, new ChatMessageContentItemCollection());
            semantic.Items.Add(new FunctionCallContent("inspect", "plugin", "call-1", new KernelArguments()));
        }
        else
        {
            semantic.Items.Add(new FunctionResultContent("inspect", "plugin", "call-1", "result"));
        }
        request.Messages.Add(new AIChatMessage
        {
            Role = kind == 2 ? "assistant" : kind == 1 ? "tool" : "user",
            Content = "Current",
            SemanticContent = kind > 0 ? semantic : null
        });

        var parameters = AnthropicAIProvider.BuildMessageParameters(request, "claude-fixture");
        var serialized = System.Text.Json.JsonSerializer.Serialize(parameters);
        var body = JObject.Parse(serialized);
        var markers = body.Descendants().OfType<JProperty>().Where(x => x.Name == "cache_control").ToArray();
        Assert.AreEqual(2, markers.Count(x => x.Value.Type != JTokenType.Null), serialized);
        Assert.IsNotNull(body["system"]![1]!["cache_control"]);
        var last = body["messages"]!.Last!["content"]!.Last!;
        Assert.AreEqual("ephemeral", last["cache_control"]!["type"]!.Value<string>());
        Assert.AreEqual(kind == 2 ? "tool_use" : kind == 1 ? "tool_result" : "text", last["type"]!.Value<string>());
        Assert.IsTrue(request.SystemMessages.All(x => x.CacheBehavior == AIChatCacheBehavior.PreferCache));
        Assert.AreEqual(AIChatCacheBehavior.Default, request.Messages.Last().CacheBehavior);
    }
}
