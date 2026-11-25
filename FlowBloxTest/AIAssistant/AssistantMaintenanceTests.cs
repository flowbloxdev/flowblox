using Anthropic.SDK.Messaging;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using ModelPricing = FlowBlox.AIAssistant.Models.ModelPricing;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantMaintenanceTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeMaintenancePrefix_MatchesNormalRequestAndAnthropicUsesAtMostFourBreakpoints(bool emptyEntry)
    {
        var history = History();
        if (emptyEntry)
            history.SessionMessages.Insert(1, new AssistantSingleMessage());
        var provider = new AnthropicAIProvider();
        var normal = AssistantChatRequestBuilder.Build("System", "Bootstrap", "Old memory", history.SessionMessages,
            "Current task", 20, 2, provider,
            new AssistantTokenBudget { MaxContextTokens = 1000000, ApproximateCharactersPerToken = 1 },
            runtimeSystemContents: ["Existing retained guidance"]).Request;
        var prefix = AssistantChatRequestBuilder.BuildMaintenancePrefix("System", "Bootstrap", "Old memory",
            history.SessionMessages, 0, history.SessionMessages.Count - 2, provider, ["Existing retained guidance"]);
        var normalParameters = AnthropicAIProvider.BuildMessageParameters(normal, "fixture");
        var prefixParameters = AnthropicAIProvider.BuildMessageParameters(prefix, "fixture");
        Assert.AreEqual(JsonConvert.SerializeObject(normalParameters.System), JsonConvert.SerializeObject(prefixParameters.System));
        CollectionAssert.AreEqual(normalParameters.Messages.Take(prefix.Messages.Count)
                .SelectMany(message => message.Content).Select(ContentIdentity).ToArray(),
            prefixParameters.Messages.SelectMany(message => message.Content).Select(ContentIdentity).ToArray());
        CollectionAssert.AreEqual(normalParameters.Messages.Take(prefix.Messages.Count).Select(message => message.Role).ToArray(),
            prefixParameters.Messages.Select(message => message.Role).ToArray());
        var markers = normalParameters.System.Count(block => block.CacheControl != null) +
            normalParameters.Messages.SelectMany(message => message.Content).Count(block => block.CacheControl != null);
        Assert.AreEqual(4, markers);
        var call = prefixParameters.Messages.SelectMany(message => message.Content).OfType<ToolUseContent>().Single();
        Assert.AreEqual(emptyEntry ? "flowblox_tool_call_2_0" : "flowblox_tool_call_1_0", call.Id);
        Assert.IsFalse(prefix.GetInputText().Contains("Protected recent", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Promotion_LeavesNativeHistoryPayloadsAndCallIdsUnchanged()
    {
        var history = History();
        var provider = new AnthropicAIProvider();
        var before = AssistantChatRequestBuilder.BuildMaintenancePrefix("System", "Bootstrap", "Old memory",
            history.SessionMessages, 0, 4, provider, []);
        var originals = history.SessionMessages.Select(message => message.CompleteMessage).ToArray();
        AssistantHistoryRetention.Promote(history.SessionMessages, history.RetainedContents);
        var after = AssistantChatRequestBuilder.BuildMaintenancePrefix("System", "Bootstrap", "Old memory",
            history.SessionMessages, 0, 4, provider, []);
        var beforeParameters = AnthropicAIProvider.BuildMessageParameters(before, "fixture");
        var afterParameters = AnthropicAIProvider.BuildMessageParameters(after, "fixture");
        CollectionAssert.AreEqual(beforeParameters.Messages.SelectMany(message => message.Content).Select(ContentIdentity).ToArray(),
            afterParameters.Messages.SelectMany(message => message.Content).Select(ContentIdentity).ToArray());
        CollectionAssert.AreEqual(originals, history.SessionMessages.Select(message => message.CompleteMessage).ToArray());
        Assert.AreEqual("Exact retained guidance", history.RetainedContents.Single().Content);
    }

    [TestMethod]
    public async Task SummaryAndCompaction_KeepOldPrefixAndPublishSummaryAndRetentionTogether()
    {
        var history = History();
        var originals = history.SessionMessages.Select(message => message.CompleteMessage).ToArray();
        var executor = new ToolResultCompactionExecutor();
        var configuration = Configuration();
        var service = CreateService(executor, configuration, history);
        executor.SummaryResponse = _ =>
        {
            AssertUncommitted(service);
            return new AiExecutorResult { Success = true, OutputText = new string('s', 200) };
        };
        executor.CompactionResponse = request =>
        {
            AssertUncommitted(service);
            Assert.AreEqual("Summary", request.Meta["CompactionKind"]);
            return new AiExecutorResult { Success = true, OutputText = "Memory" };
        };
        try
        {
            var result = await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.IsTrue(result.Success);
            var summary = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantSummary");
            var compaction = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantCompaction");
            CollectionAssert.AreEqual(summary.SystemMessages.Select(message => message.Content).ToArray(),
                compaction.SystemMessages.Select(message => message.Content).ToArray());
            CollectionAssert.AreEqual(summary.Messages.Select(message => message.Content).ToArray(),
                compaction.Messages.Take(summary.Messages.Count).Select(message => message.Content).ToArray());
            Assert.AreEqual(new string('s', 200), compaction.Messages[summary.Messages.Count].Content);
            Assert.AreEqual("assistant", compaction.Messages[summary.Messages.Count].Role);
            Assert.AreEqual(1, summary.Messages.Count(message => message.Content.StartsWith(AssistantMaintenanceRequestBuilder.ModeMarker)));
            var saved = Save(service);
            Assert.AreEqual("Memory", saved.ConversationSummary);
            Assert.AreEqual(2, saved.SummarizedMessageCount);
            Assert.AreEqual("Exact retained guidance", saved.RetainedContents.Single().Content);
            CollectionAssert.AreEqual(originals, saved.SessionMessages.Take(4).Select(message => message.CompleteMessage).ToArray());
            Assert.IsFalse(summary.SystemMessages.Any(message => message.Content == "Exact retained guidance"));
            Assert.IsTrue(executor.ChatRequests.Single().SystemMessages.Any(message => message.Content == "Exact retained guidance"));
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task CancelingCompaction_DoesNotPublishSuccessfulSummaryOrPendingRetention()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new ToolResultCompactionExecutor
        {
            SummaryResponse = _ => new AiExecutorResult { Success = true, OutputText = new string('s', 200) },
            CompactionResponse = _ =>
            {
                cancellation.Cancel();
                return new AiExecutorResult { Success = true, OutputText = "Memory" };
            }
        };
        var service = CreateService(executor, Configuration(), History());
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecutePromptAsync("Continue", cancellation.Token));
            AssertUncommitted(service);
            Assert.AreEqual(4, Save(service).SessionMessages.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task OversizedSharedPrefix_UsesTargetedSummaryWithoutDroppingHistory()
    {
        var configuration = Configuration();
        configuration.MaxContextTokens = 4096;
        var executor = new ToolResultCompactionExecutor();
        var service = CreateService(executor, configuration, History());
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            var summary = executor.Requests.First(request => request.Source == "FlowBloxAIAssistantSummary");
            Assert.AreEqual(1, summary.SystemMessages.Count);
            StringAssert.Contains(summary.GetInputText(), "Exact retained guidance");
            Assert.IsFalse(summary.GetInputText().Contains("Protected recent", StringComparison.Ordinal));
            Assert.AreEqual(6, Save(service).SessionMessages.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task KnownCacheReadPrices_KeepSummaryPrefixButChooseCheaperTargetedCompaction()
    {
        var configuration = Configuration();
        configuration.Providers.Add(new AnthropicAIProvider { Name = "Fixture", DefaultModel = "fixture" });
        configuration.SelectedProviderName = "Fixture";
        var catalogue = Substitute.For<IModelPricingCatalogueService>();
        catalogue.GetPricingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<ModelPricing?>(new()
        {
            InputCostPerToken = 1m,
            OutputCostPerToken = 1m,
            CacheReadCostPerToken = 0.5m,
            CacheWriteCostPerToken = 1.25m
        }));
        var history = History();
        ((AssistantSingleMessage)history.SessionMessages[0]).Message += new string('x', 50000);
        var executor = new ToolResultCompactionExecutor
        {
            SummaryResponse = _ => new AiExecutorResult { Success = true, OutputText = new string('s', 200) },
            CompactionResponse = _ => new AiExecutorResult { Success = true, OutputText = "Memory" }
        };
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration, pricingCatalogue: catalogue);
        service.RestoreSession(history);
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            var summary = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantSummary");
            var compaction = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantCompaction");
            Assert.AreEqual(2, summary.SystemMessages.Count);
            Assert.AreEqual(1, compaction.SystemMessages.Count);
            Assert.IsFalse(compaction.GetInputText().Contains(new string('x', 100), StringComparison.Ordinal));
            Assert.AreEqual("Memory", Save(service).ConversationSummary);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public void SummaryPrefix_PreservesCompactedContextAndAddsOriginalSourceAtTheEnd()
    {
        var history = History();
        var pair = (AssistantMessagePair)history.SessionMessages[1];
        pair.RetainedContents.Clear();
        pair.CompactedToolResults[0] = "Short projection";
        var prefix = AssistantChatRequestBuilder.BuildMaintenancePrefix("System", "Bootstrap", "Old memory",
            history.SessionMessages, 0, 2, null, []);
        var summary = AssistantSummaryRequestBuilder.Build("Old memory", history.SessionMessages.Take(2).ToArray(), [], prefix);
        CollectionAssert.AreEqual(prefix.Messages.Select(message => message.Content).ToArray(),
            summary.Messages.Take(prefix.Messages.Count).Select(message => message.Content).ToArray());
        StringAssert.Contains(summary.Messages.Last().Content, pair.ToolApiResponse);
        StringAssert.Contains(prefix.GetInputText(), "Short projection");
        Assert.AreEqual("Short projection", pair.CompactedToolResults[0]);
    }

    private static AssistantConfiguration Configuration() => new()
    {
        MaxLatestMessages = 2,
        MinLatestMessages = 2,
        SummaryCompactionRate = 1d,
        MaxContextTokens = 1000000,
        ReservedResponseTokens = 0,
        ApproximateCharactersPerToken = 1,
        MaxSummaryTokens = 20,
        SummaryCompactionTargetRatio = 0.5d,
        EnableAutomaticAdjustment = false,
        AttachProjectJsonAutomatically = false
    };

    private static string ContentIdentity(ContentBase block)
    {
        var value = JObject.Parse(System.Text.Json.JsonSerializer.Serialize(block, block.GetType()));
        if (block is ToolResultContent result)
            value["content"] = new JArray(result.Content.Select(content => JObject.Parse(ContentIdentity(content))));
        return value.ToString(Formatting.None);
    }

    private static AiAssistantHistoryDocument History() => new()
    {
        ConversationSummary = "Old memory",
        SessionMessages =
        [
            new AssistantSingleMessage { MessageRole = "user", Message = "Older task" },
            new AssistantMessagePair
            {
                AssistantRequest = """{"assistantMessage":"Read guidance","final":false,"toolCalls":[{"toolName":"LoadExplanation","arguments":{"key":"fixture"}}]}""",
                ToolApiResponse = """{"response":{"Ok":true,"Result":{"content":"Exact retained guidance"}}}""",
                RetainedContents = [new() { Key = "explanation:fixture", Kind = AssistantSystemMessageKind.Explanation,
                    Content = "Exact retained guidance", ResultIndex = 0 }]
            },
            new AssistantSingleMessage { MessageRole = "user", Message = "Protected recent task" },
            new AssistantSingleMessage { MessageRole = "assistant", Message = "Protected recent result" }
        ]
    };

    private static AiAssistantService CreateService(IAiExecutor executor, AssistantConfiguration configuration, AiAssistantHistoryDocument history)
    {
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(history);
        return service;
    }

    private static AiAssistantHistoryDocument Save(AiAssistantService service)
    {
        var history = new AiAssistantHistoryDocument();
        service.UpdateHistorySessionMetadata(history);
        return history;
    }

    private static void AssertUncommitted(AiAssistantService service)
    {
        var saved = Save(service);
        Assert.AreEqual("Old memory", saved.ConversationSummary);
        Assert.AreEqual(0, saved.SummarizedMessageCount);
        Assert.AreEqual(0, saved.RetainedContents.Count);
    }
}
