using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantSummaryRetentionTests
{
    private const string AvailableKey = "type-info:Fixture.Available";
    private const string PendingKey = "type-info:Fixture.Pending";

    [TestMethod]
    [DataRow(AssistantSystemMessageKind.Explanation, false, true)]
    [DataRow(AssistantSystemMessageKind.TypeInfo, false, false)]
    [DataRow(AssistantSystemMessageKind.TypeInfo, true, true)]
    [DataRow(AssistantSystemMessageKind.ToolDefinition, false, false)]
    [DataRow(AssistantSystemMessageKind.ToolDefinition, true, true)]
    public async Task SummaryMerge_ReferencesGuaranteedPromotionsAndKeepsBelowThresholdContent(
        AssistantSystemMessageKind kind,
        bool repeated,
        bool shouldRetain)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var pair = CreatePair();
        pair.RetainedContents[0].Kind = kind;
        var available = pair.RetainedContents[0].Clone();
        var latest = new AssistantMessagePair
        {
            AssistantRequest = "Retrieve the static description again",
            ToolApiResponse = "{\"response\":{\"Ok\":true,\"Result\":{\"retainedContentKey\":\"" + AvailableKey + "\"}}}",
            RetainedContents = repeated
                ? [new AssistantRetainedContent { Key = AvailableKey, Kind = kind, ResultIndex = 0 }]
                : []
        };
        var original = pair.CompleteMessage;
        var executor = new RecordingAiExecutor();
        var configuration = CreateConfiguration();
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(new AiAssistantHistoryDocument
        {
            SessionMessages =
            [
                new AssistantSingleMessage { MessageRole = "user", Message = "Inspect fixtures" },
                pair,
                new AssistantSingleMessage { MessageRole = "user", Message = "Older task" },
                latest,
                new AssistantSingleMessage { MessageRole = "assistant", Message = "Recent result" }
            ]
        });
        executor.SummaryRequestReceived = _ =>
        {
            var snapshot = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(snapshot);
            Assert.AreEqual(0, snapshot.RetainedContents.Count);
            Assert.AreEqual(original, snapshot.SessionMessages[1].CompleteMessage);
        };
        try
        {
            var result = await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.IsTrue(result.Success);
            var summary = executor.Requests.First(request => request.Source == "FlowBloxAIAssistantSummary");
            var input = summary.GetInputText();
            Assert.IsTrue(input.Contains("AVAILABLE_SCHEMA_PAYLOAD", StringComparison.Ordinal));
            StringAssert.Contains(input, "PENDING_SCHEMA_PAYLOAD");
            StringAssert.Contains(input, "E42");
            Assert.AreEqual(shouldRetain,
                executor.ChatRequests.Single().SystemMessages.Any(message => message.Content == available.Content));
            Assert.AreEqual(original, pair.CompleteMessage);
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    public async Task CanceledSummary_DoesNotCommitPreviewedPromotion()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        using var cancellation = new CancellationTokenSource();
        var pair = CreatePair();
        pair.RetainedContents[0].Kind = AssistantSystemMessageKind.Explanation;
        var executor = new SummaryResultExecutor(true, "Summary") { SummaryReceived = cancellation.Cancel };
        var configuration = CreateConfiguration();
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(new AiAssistantHistoryDocument
        {
            SessionMessages =
            [
                new AssistantSingleMessage { MessageRole = "user", Message = "Inspect fixtures" },
                pair,
                new AssistantSingleMessage { MessageRole = "user", Message = "Recent task" },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "Recent result" }
            ]
        });
        try
        {
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecutePromptAsync("Continue", cancellation.Token));
            var saved = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(saved);
            Assert.AreEqual(0, saved.SummarizedMessageCount);
            Assert.AreEqual(0, saved.RetainedContents.Count);
            Assert.AreEqual(pair.CompleteMessage, saved.SessionMessages[1].CompleteMessage);
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    public void UnpromotedResults_StayVerbatimEvenWhenOtherSystemContentExists()
    {
        var pair = CreatePair();
        var original = pair.CompleteMessage;
        var request = AssistantSummaryRequestBuilder.Build(
            string.Empty,
            [pair],
            [new AssistantRetainedContent { Key = "explanation:fixture", Content = "Unrelated guidance" }]);

        StringAssert.Contains(request.GetInputText(), original);
        Assert.AreEqual(original, pair.CompleteMessage);
        Assert.AreEqual(original, pair.CompleteMessage);
    }

    [TestMethod]
    public void MixedBatch_KeepsOriginalPayloadsAndDiagnosticsForPrefixReuse()
    {
        var pair = CreatePair();
        var original = pair.CompleteMessage;
        var originalTags = pair.RetainedContents.Select(content => content.Content).ToArray();
        pair.CompactedToolResults[0] = "Existing context projection";
        var request = AssistantSummaryRequestBuilder.Build(string.Empty, [pair],
            [pair.RetainedContents[0].Clone()]);
        var projectedResponse = ReadSummaryResponse(request.GetInputText());
        var originalResponse = JObject.Parse(pair.ToolApiResponse);
        var results = projectedResponse["response"]!["Result"]!["batchResults"]!;

        Assert.IsTrue(JToken.DeepEquals(originalResponse["response"]!["Result"]!["batchResults"]![0], results[0]));
        Assert.IsTrue(JToken.DeepEquals(originalResponse["response"]!["Result"]!["batchResults"]![1], results[1]));
        Assert.IsTrue(JToken.DeepEquals(originalResponse["response"]!["Result"]!["batchResults"]![2], results[2]));
        Assert.AreEqual(original, pair.CompleteMessage);
        CollectionAssert.AreEqual(originalTags, pair.RetainedContents.Select(content => content.Content).ToArray());
        Assert.AreEqual("Existing context projection", pair.CompactedToolResults[0]);
    }

    [TestMethod]
    public void EmptyRetainedPayload_DoesNotEstablishAvailabilityOrRemoveOriginalSchema()
    {
        var pair = CreatePair();
        var request = AssistantSummaryRequestBuilder.Build(
            string.Empty,
            [pair],
            [new AssistantRetainedContent { Key = AvailableKey, Content = " " }]);

        StringAssert.Contains(request.GetInputText(), pair.CompleteMessage);
    }

    [TestMethod]
    public async Task SummaryMerge_UsesPersistedSystemAvailabilityWithoutResendingItsPayload()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var pair = CreatePair();
        var original = pair.CompleteMessage;
        var available = pair.RetainedContents[0].Clone();
        var executor = new RecordingAiExecutor();
        var configuration = new AssistantConfiguration
        {
            MaxLatestMessages = 2,
            MinLatestMessages = 2,
            MaxContextTokens = 1000000,
            MaxSummaryTokens = 100000,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = false
        };
        var service = new AiAssistantService(executor, new EmptyToolApi(), configurationProvider: () => configuration);
        service.RestoreSession(new AiAssistantHistoryDocument
        {
            RetainedContents = [available],
            SessionMessages =
            [
                new AssistantSingleMessage { MessageRole = "user", Message = "Inspect fixtures" },
                pair,
                new AssistantSingleMessage { MessageRole = "user", Message = "Recent task" },
                new AssistantSingleMessage { MessageRole = "assistant", Message = "Recent result" }
            ]
        });
        executor.SummaryRequestReceived = _ =>
        {
            var snapshot = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(snapshot);
            Assert.AreEqual(original, snapshot.SessionMessages[1].CompleteMessage);
        };
        try
        {
            var result = await service.ExecutePromptAsync("Continue", CancellationToken.None);
            Assert.IsTrue(result.Success);
            var summary = executor.Requests.Single(request => request.Source == "FlowBloxAIAssistantSummary");
            var input = summary.GetInputText();
            StringAssert.Contains(input, JsonConvert.SerializeObject(new[] { AvailableKey }));
            Assert.IsTrue(input.Contains("AVAILABLE_SCHEMA_PAYLOAD", StringComparison.Ordinal));
            StringAssert.Contains(input, "PENDING_SCHEMA_PAYLOAD");
            StringAssert.Contains(input, "E42");
            Assert.IsTrue(executor.ChatRequests.Single().SystemMessages.Any(message => message.Content == available.Content));
            Assert.AreEqual(original, pair.CompleteMessage);
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    private static AssistantMessagePair CreatePair()
    {
        var availableSchema = new JObject { ["fullName"] = "Fixture.Available", ["details"] = "AVAILABLE_SCHEMA_PAYLOAD" };
        var pendingSchema = new JObject { ["fullName"] = "Fixture.Pending", ["details"] = "PENDING_SCHEMA_PAYLOAD" };
        var response = new JObject
        {
            ["response"] = new JObject
            {
                ["Ok"] = false,
                ["Result"] = new JObject
                {
                    ["batchResults"] = new JArray
                    {
                        new JObject { ["toolName"] = "GetTypeKindsInfo", ["ok"] = true, ["result"] = availableSchema },
                        new JObject { ["toolName"] = "GetTypeKindsInfo", ["ok"] = true, ["result"] = pendingSchema },
                        new JObject { ["toolName"] = "Inspect", ["ok"] = false, ["error"] = "E42", ["result"] = new JObject { ["runId"] = "fixture-run" } }
                    }
                }
            }
        };
        return new AssistantMessagePair
        {
            AssistantRequest = "Inspect both fixture schemas and diagnostics",
            ToolApiResponse = response.ToString(Formatting.None),
            RetainedContents =
            [
                new AssistantRetainedContent
                {
                    Key = AvailableKey,
                    Content = "Type information:\n" + availableSchema.ToString(Formatting.None),
                    ResultIndex = 0,
                    BatchResultIndex = 0
                },
                new AssistantRetainedContent
                {
                    Key = PendingKey,
                    Content = "Type information:\n" + pendingSchema.ToString(Formatting.None),
                    ResultIndex = 0,
                    BatchResultIndex = 1
                }
            ]
        };
    }

    private static JObject ReadSummaryResponse(string message)
    {
        const string separator = "\n\nTool API response:\n";
        return JObject.Parse(message[(message.IndexOf(separator, StringComparison.Ordinal) + separator.Length)..]);
    }

    private static AssistantConfiguration CreateConfiguration() => new()
    {
        MaxLatestMessages = 2,
        MinLatestMessages = 2,
        MaxContextTokens = 1000000,
        MaxSummaryTokens = 100000,
        SummaryCompactionRate = 1d,
        EnableAutomaticAdjustment = false,
        AttachProjectJsonAutomatically = false
    };
}
