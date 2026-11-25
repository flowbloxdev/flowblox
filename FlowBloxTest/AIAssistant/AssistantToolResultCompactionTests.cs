using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;
using FlowBlox.Core.Models.FlowBlocks.AIRemote.Providers;
using FlowBloxTest.Services;
using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantToolResultCompactionTests
{
    [TestMethod]
    [DataRow(true, "user", 1000000)]
    [DataRow(false, "assistant", 100)]
    public async Task SuccessfulSummary_CompactsOnlyLargeResultsAndPreservesOriginalsAndMinimumHistory(
        bool native,
        string regularRole,
        int contextLimit)
    {
        var history = CreateHistory(700, regularRole);
        var originalPair = (AssistantMessagePair)history.SessionMessages[2];
        var originalResult = AssistantToolResultContext.GetOriginalResults(originalPair);
        var executor = new ToolResultCompactionExecutor();
        var configuration = CreateConfiguration(native, 100);
        configuration.MaxContextTokens = contextLimit;
        var service = CreateService(executor, configuration, history);
        try
        {
            var result = await service.ExecutePromptAsync("Continue debugging", CancellationToken.None);
            Assert.IsTrue(result.Success);
            Assert.AreEqual("FlowBloxAIAssistantSummary", executor.Requests[0].Source);
            Assert.AreSame(executor.ToolCompactions.Single(), executor.Requests[1]);
            Assert.AreSame(executor.ChatRequests.Single(), executor.Requests[2]);
            StringAssert.Contains(executor.ToolCompactions.Single().Messages.Single().Content, originalResult[0]);
            var saved = Save(service);
            var savedPair = (AssistantMessagePair)saved.SessionMessages[2];
            Assert.AreEqual(2, saved.SummarizedMessageCount);
            Assert.AreEqual(originalPair.CompleteMessage, savedPair.CompleteMessage);
            Assert.AreEqual(1, savedPair.CompactedToolResults.Count);
            Assert.IsTrue(savedPair.CompactedToolResults.ContainsKey(0));
            Assert.AreEqual(history.SessionMessages[3].CompleteMessage, saved.SessionMessages[3].CompleteMessage);
            var request = executor.ChatRequests.Single();
            Assert.IsTrue(request.Messages.Any(message => message.Role == regularRole && message.Content == history.SessionMessages[3].CompleteMessage));
            var tool = request.Messages.Single(message => message.Role == "tool");
            var compactedResults = AssistantToolResultContext.ParseResults(AssistantToolResultContext.GetResponse(savedPair));
            Assert.IsTrue(compactedResults[0].Value<bool>("compacted"));
            StringAssert.Contains(compactedResults[0].Value<string>("summary")!, "E42 at worker.cs:17");
            Assert.AreEqual(originalResult[1], compactedResults[1].ToString(Formatting.None));
            if (native)
            {
                var calls = request.Messages.SelectMany(message => message.SemanticContent?.Items.OfType<FunctionCallContent>() ?? []).ToArray();
                var results = tool.SemanticContent!.Items.OfType<FunctionResultContent>().ToArray();
                Assert.AreEqual(2, calls.Length);
                CollectionAssert.AreEqual(new[] { "flowblox_tool_call_2_0", "flowblox_tool_call_2_1" }, calls.Select(call => call.Id).ToArray());
                CollectionAssert.AreEqual(calls.Select(call => call.Id).ToArray(), results.Select(item => item.CallId).ToArray());
                Assert.AreEqual(compactedResults[0].ToString(Formatting.None), results[0].Result?.ToString());
                Assert.AreEqual(originalResult[1], results[1].Result?.ToString());
            }
            else
            {
                Assert.IsNull(tool.SemanticContent);
                StringAssert.Contains(tool.Content, "E42 at worker.cs:17");
            }

            Assert.AreEqual(executor.Requests.Count, service.SessionUsage.Requests);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(4000, 0)]
    [DataRow(4001, 1)]
    public async Task DefaultThreshold_AppliesToIndividualResultsAndRequiresStrictlyMoreThan4000Tokens(int size, int expected)
    {
        var executor = new ToolResultCompactionExecutor();
        var service = CreateService(executor, CreateConfiguration(), CreateHistory(size));
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.AreEqual(expected, executor.ToolCompactions.Count);
            Assert.AreEqual(expected, ((AssistantMessagePair)Save(service).SessionMessages[2]).CompactedToolResults.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(false, "failed")]
    [DataRow(true, " ")]
    public async Task UnsuccessfulSummary_DoesNotCompactRemainingToolResults(bool success, string output)
    {
        var executor = new ToolResultCompactionExecutor
        {
            SummaryResponse = _ => new AiExecutorResult { Success = success, OutputText = output }
        };
        var service = CreateService(executor, CreateConfiguration(threshold: 100), CreateHistory(700));
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.AreEqual(0, executor.ToolCompactions.Count);
            Assert.AreEqual(0, ((AssistantMessagePair)Save(service).SessionMessages[2]).CompactedToolResults.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task SummaryCompactionWithoutNewSummarization_DoesNotTriggerToolResultCompaction()
    {
        var history = CreateHistory(700);
        history.ConversationSummary = new string('s', 200);
        var configuration = CreateConfiguration(threshold: 100);
        configuration.MaxLatestMessages = 20;
        configuration.MaxSummaryTokens = 100;
        var executor = new ToolResultCompactionExecutor();
        var service = CreateService(executor, configuration, history);
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.IsTrue(executor.Requests.Any(request => request.Meta.GetValueOrDefault("CompactionKind")?.ToString() == "Summary"));
            Assert.AreEqual(0, executor.ToolCompactions.Count);
            Assert.AreEqual(0, Save(service).SummarizedMessageCount);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow("failure", 1)]
    [DataRow("empty", 1)]
    [DataRow("larger", 2)]
    public async Task UnusableCompaction_PreservesOriginalActiveResultAndBoundsRetries(string mode, int attempts)
    {
        var executor = new ToolResultCompactionExecutor
        {
            CompactionResponse = _ => new AiExecutorResult
            {
                Success = mode != "failure",
                OutputText = mode == "empty" ? " " : new string('x', 1000)
            }
        };
        var history = CreateHistory(700);
        var service = CreateService(executor, CreateConfiguration(threshold: 100), history);
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.AreEqual(attempts, executor.ToolCompactions.Count);
            Assert.AreEqual(0, ((AssistantMessagePair)Save(service).SessionMessages[2]).CompactedToolResults.Count);
            StringAssert.Contains(executor.ChatRequests.Single().Messages.Single(message => message.Role == "tool").Content,
                ((AssistantMessagePair)history.SessionMessages[2]).ToolApiResponse);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task RetryAndRestore_KeepTheCompactedProjectionStableWithoutRewritingOriginalHistory()
    {
        var history = CreateHistory(700);
        var pair = (AssistantMessagePair)history.SessionMessages[2];
        var executor = new ToolResultCompactionExecutor();
        executor.CompactionResponse = _ => new AiExecutorResult
        {
            Success = true,
            OutputText = executor.ToolCompactions.Count == 1 ? new string('a', 80) : "E42 worker.cs:17 fixture-run"
        };
        var service = CreateService(executor, CreateConfiguration(native: true, threshold: 100), history);
        AiAssistantHistoryDocument saved;
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.AreEqual(2, executor.ToolCompactions.Count);
            saved = Save(service);
        }
        finally
        {
            service.ResetSession();
        }

        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(saved))!;
        var nextExecutor = new ToolResultCompactionExecutor();
        var configuration = CreateConfiguration(native: true, threshold: 100);
        configuration.MaxLatestMessages = 20;
        service = CreateService(nextExecutor, configuration, restored);
        ((AssistantMessagePair)restored.SessionMessages[2]).CompactedToolResults[0] = "MUTATED-SNAPSHOT";
        try
        {
            await service.ExecutePromptAsync("Next", CancellationToken.None);
            Assert.AreEqual(0, nextExecutor.ToolCompactions.Count);
            var compactedPair = (AssistantMessagePair)Save(service).SessionMessages[2];
            Assert.AreEqual(pair.ToolApiResponse, compactedPair.ToolApiResponse);
            Assert.AreEqual("E42 worker.cs:17 fixture-run", compactedPair.CompactedToolResults[0]);
            var first = executor.ChatRequests.Single().Messages.Single(message => message.Role == "tool");
            var next = nextExecutor.ChatRequests.Single().Messages.Single(message => message.Role == "tool");
            Assert.AreEqual(first.Content, next.Content);
            CollectionAssert.AreEqual(
                first.SemanticContent!.Items.OfType<FunctionResultContent>().Select(result => result.CallId).ToArray(),
                next.SemanticContent!.Items.OfType<FunctionResultContent>().Select(result => result.CallId).ToArray());
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task CancellationDuringCompaction_PreservesOriginalToolResult()
    {
        using var cancellation = new CancellationTokenSource();
        var executor = new ToolResultCompactionExecutor
        {
            CompactionResponse = _ =>
            {
                cancellation.Cancel();
                return new AiExecutorResult { Success = true, OutputText = "Small result" };
            }
        };
        var service = CreateService(executor, CreateConfiguration(threshold: 100), CreateHistory(700));
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecutePromptAsync("Continue", cancellation.Token));
            var saved = Save(service);
            Assert.AreEqual(2, saved.SummarizedMessageCount);
            Assert.AreEqual(0, ((AssistantMessagePair)saved.SessionMessages[2]).CompactedToolResults.Count);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task CompactedProjection_PreservesStructuredStatusErrorsAndLogsEvenWhenModelOmitsThem()
    {
        var history = CreateHistory(700);
        var pair = (AssistantMessagePair)history.SessionMessages[2];
        var results = AssistantToolResultContext.ParseResults(pair.ToolApiResponse);
        results[0]["Ok"] = false;
        results[0]["Log"] = new JArray("Failure at worker.cs:17, fixture-run");
        pair.ToolApiResponse = string.Join("\n", results.Select(result =>
            new JObject { ["response"] = result }.ToString(Formatting.None)));
        var executor = new ToolResultCompactionExecutor
        {
            CompactionResponse = _ => new AiExecutorResult { Success = true, OutputText = "Condensed notes" }
        };
        var service = CreateService(executor, CreateConfiguration(threshold: 100), history);
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            var saved = (AssistantMessagePair)Save(service).SessionMessages[2];
            var projected = AssistantToolResultContext.ParseResults(AssistantToolResultContext.GetResponse(saved))[0];
            Assert.IsTrue(projected.Value<bool>("compacted"));
            Assert.IsFalse(projected.Value<bool>("Ok"));
            Assert.AreEqual(results[0].Value<string>("Error"), projected.Value<string>("Error"));
            Assert.IsTrue(JToken.DeepEquals(results[0]["Log"], projected["Log"]));
            Assert.AreEqual(pair.ToolApiResponse, saved.ToolApiResponse);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    public async Task LaterSuccessfulSummary_RechecksExistingProjectionIfItStillExceedsThreshold()
    {
        var history = CreateHistory(700);
        history.SessionMessages.Insert(2, new AssistantSingleMessage { MessageRole = "user", Message = "Earlier context" });
        history.SessionMessages.Add(new AssistantSingleMessage { MessageRole = "assistant", Message = "Recent context" });
        var executor = new ToolResultCompactionExecutor();
        executor.CompactionResponse = _ => new AiExecutorResult
        {
            Success = true,
            OutputText = executor.ToolCompactions.Count <= 2 ? new string('a', 160) : "E42"
        };
        var configuration = CreateConfiguration(threshold: 100);
        configuration.MaxLatestMessages = 4;
        configuration.MinLatestMessages = 4;
        var service = CreateService(executor, configuration, history);
        try
        {
            await service.ExecutePromptAsync("First", CancellationToken.None);
            Assert.AreEqual(2, executor.ToolCompactions.Count);
            Assert.AreEqual(2, Save(service).SummarizedMessageCount);
            configuration.MaxLatestMessages = 5;
            configuration.MinLatestMessages = 5;
            await service.ExecutePromptAsync("Second", CancellationToken.None);
            Assert.AreEqual(3, executor.ToolCompactions.Count);
            var saved = Save(service);
            Assert.AreEqual(3, saved.SummarizedMessageCount);
            Assert.AreEqual("E42", ((AssistantMessagePair)saved.SessionMessages[3]).CompactedToolResults[0]);
            Assert.AreEqual(history.SessionMessages[3].CompleteMessage, saved.SessionMessages[3].CompleteMessage);
        }
        finally
        {
            service.ResetSession();
        }
    }

    [TestMethod]
    [DataRow(AssistantSystemMessageKind.TypeInfo)]
    [DataRow(AssistantSystemMessageKind.Explanation)]
    public async Task MarkedToolContentIsNeverCompactedAndRemainsVerbatim(AssistantSystemMessageKind kind)
    {
        var history = CreateHistory(700);
        var pair = (AssistantMessagePair)history.SessionMessages[2];
        var original = pair.ToolApiResponse;
        pair.RetainedContents.Add(new AssistantRetainedContent
        {
            Key = kind == AssistantSystemMessageKind.Explanation ? "explanation:fixture" : "type-info:fixture",
            Content = original,
            Kind = kind,
            ResultIndex = 0
        });
        pair.CompactedToolResults[0] = "LEGACY-COMPACTED-PROJECTION";
        var executor = new ToolResultCompactionExecutor();
        var service = CreateService(executor, CreateConfiguration(native: true, threshold: 100), history);
        try
        {
            await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.AreEqual(0, executor.ToolCompactions.Count);
            var request = executor.ChatRequests.Single();
            if (kind == AssistantSystemMessageKind.Explanation)
            {
                Assert.AreEqual(original, request.SystemMessages.Last().Content);
                Assert.AreEqual(original, Save(service).RetainedContents.Single().Content);
            }
            else
            {
                StringAssert.Contains(request.Messages.Single(message => message.Role == "tool").Content, original);
                Assert.AreEqual(0, Save(service).RetainedContents.Count);
            }
            Assert.IsFalse(request.Messages.Any(message => message.Content.Contains("LEGACY-COMPACTED-PROJECTION", StringComparison.Ordinal)));
        }
        finally
        {
            service.ResetSession();
        }
    }

    private static AssistantConfiguration CreateConfiguration(bool native = false, int? threshold = null)
    {
        var configuration = new AssistantConfiguration
        {
            MaxLatestMessages = 2,
            MinLatestMessages = 2,
            MaxContextTokens = 1000000,
            MaxSummaryTokens = 100000,
            ApproximateCharactersPerToken = 1,
            SummaryCompactionRate = 1d,
            SummaryCompactionTargetRatio = 0.5d,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = false
        };
        if (threshold.HasValue)
            configuration.ToolResultCompactionThreshold = threshold.Value;

        if (native)
        {
            configuration.Providers.Add(new OpenAIProvider { Name = "Fixture", DefaultModel = "synthetic" });
            configuration.SelectedProviderName = "Fixture";
        }

        return configuration;
    }

    private static AiAssistantService CreateService(
        ToolResultCompactionExecutor executor,
        AssistantConfiguration configuration,
        AiAssistantHistoryDocument history)
    {
        var pricing = Substitute.For<IModelPricingCatalogueService>();
        pricing.GetPricingAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ModelPricing?>(null));
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration, pricingCatalogue: pricing);
        service.RestoreSession(history);
        return service;
    }

    private static AiAssistantHistoryDocument CreateHistory(int resultSize, string regularRole = "user")
    {
        var largeResult = new JObject { ["Error"] = "E42 at worker.cs:17", ["runId"] = "fixture-run", ["data"] = "" };
        largeResult["data"] = new string('x', resultSize - largeResult.ToString(Formatting.None).Length);
        var transcript = new[]
        {
            new JObject { ["tool"] = "Inspect", ["response"] = largeResult }.ToString(Formatting.None),
            new JObject { ["tool"] = "Status", ["response"] = new JObject { ["Ok"] = true } }.ToString(Formatting.None)
        };
        var instruction = new JObject
        {
            ["assistantMessage"] = new string('a', 300),
            ["final"] = false,
            ["toolCalls"] = new JArray
            {
                new JObject { ["toolName"] = "Inspect", ["arguments"] = new JObject() },
                new JObject { ["toolName"] = "Status", ["arguments"] = new JObject() }
            }
        };
        return new AiAssistantHistoryDocument
        {
            SessionMessages =
            [
                new AssistantSingleMessage { MessageRole = "user", Message = "Older task" },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "Older response" },
                new AssistantMessagePair
                {
                    AssistantRequest = instruction.ToString(Formatting.None),
                    ToolApiResponse = AssistantPromptBuilder.BuildToolApiResponsePrompt(transcript)
                },
                new AssistantSingleMessage { MessageRole = regularRole, Message = new string('u', 500) }
            ]
        };
    }

    private static AiAssistantHistoryDocument Save(AiAssistantService service)
    {
        var history = new AiAssistantHistoryDocument();
        service.UpdateHistorySessionMetadata(history);
        return history;
    }
}
