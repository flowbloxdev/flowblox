using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantHistoryRetentionTests
{
    private const string FinalResponse = """{"assistantMessage":"Done","final":true,"toolCalls":[]}""";

    [TestMethod]
    public async Task SecondRetrieval_CopiesExactSchemaAtTransitionAndPreservesOriginalHistory()
    {
        var executor = new QueuedAiExecutor(TypeRequest(), TypeRequest(), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1);
        try
        {
            var result = await service.ExecutePromptAsync("Inspect the type twice", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            var saved = SaveHistory(service);
            var retained = saved.RetainedContents.Single(content => content.Kind == AssistantSystemMessageKind.TypeInfo);
            Assert.AreEqual(AssistantSystemMessageKind.TypeInfo, retained.Kind);
            Assert.AreEqual(2, saved.SessionMessages.OfType<AssistantMessagePair>().Count());
            Assert.IsFalse(executor.ChatRequests[1].SystemMessages.Any(message => message.Content == retained.Content));
            Assert.IsTrue(executor.ChatRequests[1].Messages.Any(message => message.Role == "tool"));
            Assert.AreEqual(retained.Content, executor.ChatRequests[2].SystemMessages.Last().Content);
            Assert.IsTrue(executor.ChatRequests[2].Messages.Any(message => message.Role == "tool"));
            Assert.IsNotNull(TypeDescriptionCompactionLayerTests.Expand(JObject.Parse(retained.Content.Split('\n', 2)[1]))["kind"]!["properties"]);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task SummaryDoesNotPromoteFirstRetrieval_SecondRetrievalUsesPreservedSchema()
    {
        var history = await CreateTaggedHistoryAsync();
        var retained = history.SessionMessages[1].RetainedContents.Single();
        var executor = new QueuedAiExecutor(TypeRequest(), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1);
        service.RestoreSession(history);
        try
        {
            var result = await service.ExecutePromptAsync("Inspect again after summarization", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.IsTrue(executor.Requests.Any(request => request.Source == "FlowBloxAIAssistantSummary"));
            Assert.IsFalse(executor.ChatRequests[0].SystemMessages.Any(message => message.Content == retained.Content));
            Assert.AreEqual(retained.Content, executor.ChatRequests[1].SystemMessages.Last().Content);
            Assert.AreEqual(retained.Content, SaveHistory(service).RetainedContents.Single(content => content.Kind == AssistantSystemMessageKind.TypeInfo).Content);
            var firstSummary = executor.Requests.First(request => request.Source == "FlowBloxAIAssistantSummary");
            Assert.IsTrue(firstSummary.GetInputText().Contains(retained.Content.Split('\n', 2)[1], StringComparison.Ordinal));
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(0, "system")]
    [DataRow(2, "system")]
    public async Task RestoredHistory_PreservesPendingSchemaIndependentOfSummaryBoundary(int summarizedCount, string location)
    {
        var history = await CreateTaggedHistoryAsync();
        history.SummarizedMessageCount = summarizedCount;
        history.ConversationSummary = summarizedCount > 0 ? "Previously inspected the type" : string.Empty;
        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(history))!;
        var executor = new QueuedAiExecutor(TypeRequest(), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1);
        service.RestoreSession(restored);
        restored.SessionMessages[1].RetainedContents.Single().Content = "MUTATED-SAVED-HISTORY";
        try
        {
            var result = await service.ExecutePromptAsync("Inspect after reopening", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            var saved = SaveHistory(service);
            var retained = saved.RetainedContents.Single(content => content.Kind == AssistantSystemMessageKind.TypeInfo);
            Assert.AreEqual(history.SessionMessages[1].RetainedContents.Single().Content, retained.Content);
            Assert.AreEqual(retained.Content, executor.ChatRequests.Last().SystemMessages.Last().Content);
            Assert.AreEqual(2, saved.SessionMessages.OfType<AssistantMessagePair>().Count());
            Assert.AreEqual("system", location);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(false, "failed")]
    [DataRow(true, " ")]
    public async Task FailedOrEmptySummary_DefersSecondRetrievalPromotionUntilAContextTransition(bool success, string output)
    {
        var executor = new SummaryResultExecutor(success, output, TypeRequest(), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1);
        service.RestoreSession(await CreateTaggedHistoryAsync());
        try
        {
            var result = await service.ExecutePromptAsync("Inspect omitted type", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            var saved = SaveHistory(service);
            Assert.AreEqual(0, saved.SummarizedMessageCount);
            Assert.AreEqual(0, saved.RetainedContents.Count(content => content.Kind == AssistantSystemMessageKind.TypeInfo));
            Assert.AreEqual(2, saved.SessionMessages.OfType<AssistantMessagePair>().Count());
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task CanceledSummary_DoesNotAdvanceRetentionBoundary()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new SummaryResultExecutor(true, "Summary")
        {
            SummaryReceived = () => cancellation.Cancel()
        };
        var service = CreateService(executor, maxLatestMessages: 1);
        service.RestoreSession(await CreateTaggedHistoryAsync());
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecutePromptAsync("Inspect", cancellation.Token));
            Assert.AreEqual(0, SaveHistory(service).SummarizedMessageCount);
            Assert.AreEqual(0, service.GetDebugSnapshot().SystemMessages.Count(message => message.Kind == AssistantSystemMessageKind.TypeInfo));
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PartiallyFailedBatch_ExtractsRepeatedTypeWhilePreservingUnrelatedFailure(bool native)
    {
        var arguments = new JObject
        {
            ["requests"] = new JArray
            {
                new JObject { ["toolName"] = "GetTypeKindsInfo", ["arguments"] = TypeArguments() },
                new JObject { ["toolName"] = "GetTypeKindsInfo", ["arguments"] = TypeArguments() },
                new JObject { ["toolName"] = "UnregisteredFixtureTool", ["arguments"] = new JObject() }
            }
        };
        var executor = new QueuedAiExecutor(ToolRequest("BatchExecuteToolRequests", arguments), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1, native: native);
        try
        {
            var result = await service.ExecutePromptAsync("Inspect batch", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            var saved = SaveHistory(service);
            var pair = saved.SessionMessages.OfType<AssistantMessagePair>().Single();
            var payload = ReadResult(pair);
            Assert.IsFalse(payload.Value<bool>("allOk"));
            var retained = saved.RetainedContents.Single(content => content.Kind == AssistantSystemMessageKind.TypeInfo);
            Assert.IsNotNull(payload["batchResults"]![0]!["result"]!["kind"]);
            Assert.AreEqual("history", payload["batchResults"]![1]!["result"]!.Value<string>("location"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(payload["batchResults"]![2]!.Value<string>("error")));
            Assert.AreEqual(retained.Content, pair.RetainedContents.First().Content);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public void PromotionAppendsAtTransitionAndPreservesEveryHistoryEntry()
    {
        var messages = new List<AssistantSessionMessage>
        {
            MarkedPair("explanation:first", "First explanation", AssistantSystemMessageKind.Explanation),
            MarkedPair("type-info:A", "A schema", AssistantSystemMessageKind.TypeInfo)
        };
        var retained = new List<AssistantRetainedContent>();
        AssistantHistoryRetention.Promote(messages, retained);
        CollectionAssert.AreEqual(new[] { "First explanation" }, retained.Select(content => content.Content).ToArray());
        Assert.AreEqual(2, messages.Count);
        messages.Add(MarkedPair("explanation:second", "Second explanation", AssistantSystemMessageKind.Explanation));
        messages.Add(MarkedPair("type-info:A", string.Empty, AssistantSystemMessageKind.TypeInfo));
        AssistantHistoryRetention.Promote(messages, retained);
        CollectionAssert.AreEqual(new[] { "First explanation", "Second explanation", "A schema" }, retained.Select(content => content.Content).ToArray());
        Assert.AreEqual(4, messages.Count);
        var request = AssistantChatRequestBuilder.Build("System", "Bootstrap", string.Empty, messages, "Continue", 20, 2, null,
            new AssistantTokenBudget { MaxContextTokens = 100000, ReservedResponseTokens = 0 },
            runtimeSystemContents: retained.Select(content => content.Content).ToArray());
        Assert.AreEqual(4, request.IncludedHistoryMessageCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterleavedGuidanceKeepsItsAppendOrderAndSurvivesRestore(bool native)
    {
        var first = ToolRequest("LoadExplanation", new JObject { ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey });
        var second = ToolRequest("LoadExplanation", new JObject { ["key"] = AssistantPromptCatalog.SpecialTestDefinitionsKey });
        var executor = new QueuedAiExecutor(first, TypeRequest(), TypeRequest(), second, FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1, native: native);
        AiAssistantHistoryDocument saved;
        try
        {
            var result = await service.ExecutePromptAsync("Inspect interleaved guidance", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            saved = SaveHistory(service);
            CollectionAssert.AreEqual(new[] { AssistantSystemMessageKind.ToolDefinition, AssistantSystemMessageKind.Explanation, AssistantSystemMessageKind.TypeInfo,
                AssistantSystemMessageKind.Explanation }, saved.RetainedContents.Select(content => content.Kind).ToArray());
            var contents = saved.RetainedContents.Skip(1).Select(content => content.Content).ToArray();
            Assert.AreEqual(contents[0], executor.ChatRequests[1].SystemMessages.Last().Content);
            CollectionAssert.AreEqual(contents.Take(2).ToArray(), executor.ChatRequests[3].SystemMessages.Skip(3).Select(message => message.Content).ToArray());
            CollectionAssert.AreEqual(contents, executor.ChatRequests[4].SystemMessages.Skip(3).Select(message => message.Content).ToArray());
            Assert.AreEqual(4, saved.SessionMessages.OfType<AssistantMessagePair>().Count());
            Assert.IsTrue(executor.ChatRequests[4].Messages.Any(message => message.Role == "tool"));
        }
        finally
        {
            service.ResetSession();
        }
        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(saved))!;
        var nextExecutor = new QueuedAiExecutor(FinalResponse);
        service = CreateService(nextExecutor, native: native);
        service.RestoreSession(restored);
        restored.RetainedContents[0].Content = "MUTATED-SAVED-HISTORY";
        try
        {
            await service.ExecutePromptAsync("Continue after restore", CancellationToken.None);
            CollectionAssert.AreEqual(saved.RetainedContents.Select(content => content.Content).ToArray(),
                nextExecutor.ChatRequests.Single().SystemMessages.Skip(2).Select(message => message.Content).ToArray());
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task LegacyHistoryMarkersAreReadAndPromotedWithoutLosingTheirPayload()
    {
        var history = await CreateTaggedHistoryAsync();
        var expected = history.SessionMessages[1].RetainedContents.Single().Content;
        var json = JsonConvert.SerializeObject(history).Replace("\"RetainedContents\":", "\"RetainOnSummarization\":", StringComparison.Ordinal);
        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(json)!;
        restored.RetainedContents = [KnownToolContract()];
        var executor = new QueuedAiExecutor(TypeRequest(), FinalResponse);
        var service = CreateService(executor, maxLatestMessages: 1);
        service.RestoreSession(restored);
        try
        {
            await service.ExecutePromptAsync("Inspect legacy schema", CancellationToken.None);
            Assert.AreEqual(expected, SaveHistory(service).RetainedContents.Single(content => content.Kind == AssistantSystemMessageKind.TypeInfo).Content);
            Assert.AreEqual(2, SaveHistory(service).SessionMessages.OfType<AssistantMessagePair>().Count());
        }
        finally
        {
            service.ResetSession();
        }
    }

    private static AssistantMessagePair MarkedPair(string key, string content, AssistantSystemMessageKind kind)
    {
        return new AssistantMessagePair
        {
            AssistantRequest = "Inspect metadata",
            ToolApiResponse = new JObject { ["response"] = new JObject
            {
                ["Ok"] = true,
                ["Result"] = new JObject { ["data"] = content }
            } }.ToString(Formatting.None),
            RetainedContents = [new() { Key = key, Content = content, Kind = kind, ResultIndex = 0 }]
        };
    }

    private static AiAssistantService CreateService(IAiExecutor executor, int maxLatestMessages = 20, bool native = false)
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var configuration = new AssistantConfiguration
        {
            MaxToolRounds = 8,
            MaxLatestMessages = maxLatestMessages,
            MinLatestMessages = 1,
            MaxContextTokens = 1000000,
            SummaryCompactionRate = 1d,
            MaxSummaryTokens = 100000,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = false
        };
        if (native)
        {
            configuration.Providers.Add(new OpenAIProvider { Name = "Fixture", DefaultModel = "synthetic" });
            configuration.SelectedProviderName = "Fixture";
        }
        var service = new AiAssistantService(executor, new DefaultToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(new AiAssistantHistoryDocument { RetainedContents = [KnownToolContract()] });
        return service;
    }

    private static async Task<AiAssistantHistoryDocument> CreateTaggedHistoryAsync()
    {
        var response = await new GetTypeKindsInfoHandler().HandleAsync(TypeArguments(), CancellationToken.None);
        Assert.IsTrue(response.Ok, response.Error);
        return new AiAssistantHistoryDocument
        {
            RetainedContents = [KnownToolContract()],
            SessionMessages =
            [
                new AssistantSingleMessage { MessageRole = "user", Message = "Inspect the type" },
                new AssistantMessagePair
                {
                    AssistantRequest = TypeRequest(),
                    ToolApiResponse = new JObject { ["response"] = JToken.FromObject(response) }.ToString(Formatting.None),
                    RetainedContents = response.RetainedContents
                },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "Inspected" }
            ]
        };
    }

    private static AiAssistantHistoryDocument SaveHistory(AiAssistantService service)
    {
        var history = new AiAssistantHistoryDocument();
        service.UpdateHistorySessionMetadata(history);
        return history;
    }

    private static AssistantRetainedContent KnownToolContract()
    {
        var definition = new GetTypeKindsInfoHandler().Definition;
        return new AssistantRetainedContent
        {
            Key = GetToolDefinitionsHandler.GetRetentionKey(definition.Name),
            Title = definition.Name,
            Kind = AssistantSystemMessageKind.ToolDefinition,
            Content = "Tool contract:\n" + new JObject
            {
                ["name"] = definition.Name,
                ["description"] = definition.Description,
                ["argumentsSchema"] = definition.ArgumentsSchema
            }.ToString(Formatting.None)
        };
    }

    private static JObject ReadResult(AssistantMessagePair pair) =>
        pair.ToolApiResponse.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith('{'))
            .Select(JObject.Parse)
            .Single()["response"]!["Result"]!.ToObject<JObject>()!;

    private static JObject TypeArguments() => new() { ["typeFullName"] = typeof(WebBrowserFlowBlock).FullName };

    private static string TypeRequest() => ToolRequest("GetTypeKindsInfo", TypeArguments());

    private static string ToolRequest(string name, JObject arguments) => new JObject
    {
        ["assistantMessage"] = "Inspect metadata",
        ["final"] = false,
        ["toolCalls"] = new JArray { new JObject { ["toolName"] = name, ["arguments"] = arguments } }
    }.ToString(Formatting.None);

}
