using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.State;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util.Compression;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class AssistantSessionContentCacheTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task DeclaringTypePromotion_NormalizesEntireRoundAndSurvivesSummarization(bool batch)
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var executor = new QueuedAiExecutor(
            Round(
                batch,
                typeof(StartFlowBlock),
                typeof(TableReaderFlowBlock)),
            Round(batch, typeof(WebBrowserFlowBlock)),
            Round(
                batch,
                typeof(TableReaderFlowBlock),
                typeof(WebBrowserFlowBlock)),
            "{\"assistantMessage\":\"Done\",\"final\":true,\"toolCalls\":[]}",
            Round(batch, typeof(TableReaderFlowBlock)),
            "{\"assistantMessage\":\"Done\",\"final\":true,\"toolCalls\":[]}");
        var service = new AiAssistantService(
            executor,
            new DefaultToolApi(),
            configurationProvider: () => new AssistantConfiguration
            {
                MaxToolRounds = 4,
                MaxLatestMessages = 1,
                MinLatestMessages = 1,
                SummaryCompactionRate = 1,
                MaxContextTokens = 200000,
                EnableAutomaticAdjustment = false
            });
        try
        {
            var result = await service.GenerateProjectAsync("Build the flow", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join("\n", result.Errors));
            Assert.AreEqual(4, executor.ChatRequests.Count);
            Assert.IsTrue(executor.Requests.Any(x => x.Source == "FlowBloxAIAssistantSummary"));
            var history = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(history);
            Assert.IsTrue(history.SummarizedMessageCount > 0);
            var rounds = history.SessionMessages.OfType<AssistantMessagePair>()
                .ToArray();
            var first = Results(rounds[0]);
            Assert.AreEqual(2, first.Length);
            // TableReader promotes BaseFlowBlock. The earlier Start result is rendered again without counting it twice.
            AssertOmitted(
                first[0],
                typeof(BaseFlowBlock),
                "Name");
            AssertOmitted(
                first[1],
                typeof(BaseFlowBlock),
                "Name");
            Assert.IsTrue(Properties(first[1]).Any(x => x.Value<string>("name") == "MappingEntries"));
            Assert.IsNotNull(first[0]["kind"], "Rendering must not count StartFlowBlock as a second retrieval.");
            var afterFirst = SystemTypes(executor.ChatRequests[1]);
            Assert.IsTrue(afterFirst.ContainsKey(Compact(typeof(BaseFlowBlock))));
            Assert.IsFalse(afterFirst.ContainsKey(Compact(typeof(StartFlowBlock))));
            Assert.IsFalse(afterFirst.ContainsKey(Compact(typeof(TableReaderFlowBlock))));

            var browser = Results(rounds[1])
                .Single();
            AssertOmitted(
                browser,
                typeof(BaseFlowBlock),
                "Name");
            AssertOmitted(
                browser,
                typeof(BaseResultFlowBlock),
                "OutputIgnoreDuplicates");
            var secondRequest = executor.Requests.IndexOf(executor.ChatRequests[1]);
            var thirdRequest = executor.Requests.IndexOf(executor.ChatRequests[2]);
            Assert.IsTrue(
                executor.Requests.Skip(secondRequest + 1)
                    .Take(thirdRequest - secondRequest - 1)
                    .Any(x => x.Source == "FlowBloxAIAssistantSummary"),
                "The scenario must summarize between WebBrowser and the repeated type calls.");
            var afterSecond = SystemTypes(executor.ChatRequests[2]);
            Assert.IsTrue(afterSecond.ContainsKey(Compact(typeof(BaseResultFlowBlock))));
            Assert.IsFalse(afterSecond.ContainsKey(Compact(typeof(BaseSingleResultFlowBlock))));
            Assert.IsFalse(afterSecond.ContainsKey(Compact(typeof(WebBrowserFlowBlock))));

            foreach (var repeated in Results(rounds[2]))
            {
                Assert.IsNull(repeated["kind"]);
            }

            var afterThird = SystemTypes(executor.ChatRequests[3]);
            Assert.IsTrue(afterThird.ContainsKey(Compact(typeof(TableReaderFlowBlock))));
            Assert.IsTrue(afterThird.ContainsKey(Compact(typeof(WebBrowserFlowBlock))));
            Assert.IsTrue(afterThird.ContainsKey(Compact(typeof(BaseSingleResultFlowBlock))));
            AssertOmitted(
                new JObject
                {
                    ["kind"] = afterThird[Compact(typeof(WebBrowserFlowBlock))]
                },
                typeof(BaseResultFlowBlock),
                "OutputIgnoreDuplicates");
            Assert.IsFalse(Properties(new JObject
            {
                ["kind"] = afterThird[Compact(typeof(TableReaderFlowBlock))]
            })
                .Any(x => x.Value<string>("name") == "OutputIgnoreDuplicates"));
            var bindings = service.GetDebugSnapshot().SystemMessages.Where(x => x.IsRuntimeBound)
                .ToArray();
            Assert.AreEqual(bindings.Length, bindings.Select(x => x.Key)
                .Distinct().Count());
            Assert.IsFalse(bindings.Any(x => x.Key == "type:" + typeof(StartFlowBlock).FullName));
            Assert.IsTrue(executor.ChatRequests[3].SystemMessages.Skip(2).All(x =>
                x.CacheBehavior == FlowBlox.Core.Models.FlowBlocks.AIRemote.Base.AIChatCacheBehavior.PreferCache));

            service.RestoreSession(history);
            Assert.IsFalse(service.GetDebugSnapshot().SystemMessages.Any(x => x.IsRuntimeBound));
            var resumed = await service.GenerateProjectAsync("Retrieve TableReader after restore", CancellationToken.None);
            Assert.IsTrue(resumed.Success, string.Join("\n", resumed.Errors));
            var fresh = Results((AssistantMessagePair)service.GetDebugSnapshot().History.Select(x => x.Message)
                .OfType<AssistantMessagePair>()
                .Last());
            Assert.IsNotNull(
                fresh.Single()["kind"],
                "Old history claims must not prevent a fresh retrieval after restore.");
            Assert.IsFalse(service.GetDebugSnapshot().SystemMessages.Any(x => x.IsRuntimeBound));
        }
        finally { service.ResetSession(); }
    }

    [TestMethod]
    public async Task Rendering_IsPure_AndOverrideDeclarationsAreComplete()
    {
        var session = Guid.NewGuid().ToString("N");
        ToolHandlerUtilities.SetCurrentSessionGuid(session);
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        try
        {
            var response = await TypeInfo(typeof(WebBrowserFlowBlock));
            var nameProperties = Properties(response.Result)
                .Where(x => x.Value<string>("name") == "Name")
                .ToArray();
            Assert.IsTrue(nameProperties.Length >= 2);
            Assert.IsTrue(nameProperties.All(x => x["declaringType"] != null));
            CollectionAssert.Contains(
                nameProperties.Select(x => x.Value<string>("declaringType"))
                    .ToArray(),
                Compact(typeof(BaseSingleResultFlowBlock)));
            for (var i = 0; i < 3; i++)
                response.RefreshForSerialization();

            Assert.AreEqual(0, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
            Assert.IsNotNull(response.Result["kind"]);
            await TypeInfo(typeof(WebBrowserFlowBlock));
            var system = ToolHandlerUtilities.CurrentContentCache.GetSystemMessages();
            foreach (var type in new[] { typeof(BaseFlowBlock),
                typeof(BaseSingleResultFlowBlock) })
            {
                var kind = JObject.Parse(system.Single(x => x.Key == "type:" + type.FullName).Content)["kind"]!;
                Assert.IsTrue(kind["properties"]!.Children<JObject>().Any(x => x.Value<string>("name") == "Name"));
            }
        }
        finally { ToolHandlerUtilities.ClearSessionCache(session); ToolHandlerUtilities.SetCurrentSessionGuid(null); }
    }

    [TestMethod]
    public async Task NestedReactiveObjects_UseTheSameDeclaringTypePromotion()
    {
        var session = Guid.NewGuid().ToString("N");
        ToolHandlerUtilities.SetCurrentSessionGuid(session);
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        try
        {
            var first = await TypeInfo(typeof(ParentFixture));
            Assert.IsTrue(first.Result["additionalTypeKindsInfo"]!.Children<JObject>().Any(x => x.Value<string>("fullName") == typeof(NestedFixture).FullName));
            await TypeInfo(typeof(ParentFixture));
            Assert.IsTrue(ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Any(x => x.Key == "type:" + typeof(NestedFixture).FullName));
            Assert.IsTrue(ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Any(x => x.Key == "type:" + typeof(NestedBaseFixture).FullName));
        }
        finally { ToolHandlerUtilities.ClearSessionCache(session); ToolHandlerUtilities.SetCurrentSessionGuid(null); }
    }

    [TestMethod]
    public async Task TypeHandlers_ShareCounters_AndInvalidRequestsDoNotCount()
    {
        var session = Guid.NewGuid().ToString("N");
        ToolHandlerUtilities.SetCurrentSessionGuid(session);
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        try
        {
            var managed = new GetManagedObjectKindsInfoHandler();
            Assert.IsFalse((await managed.HandleAsync(
                new JObject
                {
                    ["typeFullName"] = typeof(TableReaderFlowBlock).FullName
                },
                CancellationToken.None)).Ok);
            Assert.IsNotNull((await TypeInfo(typeof(TableReaderFlowBlock))).Result["kind"]);
            Assert.IsNotNull((await TypeInfo(typeof(FieldElement))).Result["kind"]);
            var response = await managed.HandleAsync(new JObject
            {
                ["typeFullName"] = typeof(FieldElement).FullName
            }, CancellationToken.None);
            Assert.IsTrue(response.Ok);
            Assert.IsNull(response.Result["kind"]);
            Assert.IsTrue(ToolHandlerUtilities.CurrentContentCache.IsTypePromoted(typeof(FieldElement).FullName!));
        }
        finally { ToolHandlerUtilities.ClearSessionCache(session); ToolHandlerUtilities.SetCurrentSessionGuid(null); }
    }

    [TestMethod]
    public async Task ConcurrentSessions_IsolatePromotions_AndResetRestartsRetrievals()
    {
        var sessions = new[] { Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N") };
        try
        {
            await Task.WhenAll(sessions.Select(async session =>
            {
                ToolHandlerUtilities.SetCurrentSessionGuid(session);
                await Task.Yield();
                var args = new JObject
                {
                    ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey
                };
                var handler = new LoadExplanationHandler();
                await handler.HandleAsync(args, CancellationToken.None);
                Assert.AreEqual(1, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
                Assert.AreEqual(session, ToolHandlerUtilities.CurrentSessionGuid);
            }));
            ToolHandlerUtilities.ClearSessionCache(sessions[0]);
            Assert.AreEqual(0, AssistantSessionContentStore.Get(sessions[0]).GetSystemMessages().Count);
            Assert.AreEqual(1, AssistantSessionContentStore.Get(sessions[1]).GetSystemMessages().Count);
            ToolHandlerUtilities.SetCurrentSessionGuid(sessions[0]);
            var fresh = await new LoadExplanationHandler().HandleAsync(
                new JObject
                {
                    ["key"] = AssistantPromptCatalog.SpecialGeneratorsKey
                },
                CancellationToken.None);
            Assert.AreEqual(true, fresh.Result.Value<bool>("loaded"));
            Assert.AreEqual(1, ToolHandlerUtilities.CurrentContentCache.GetSystemMessages().Count);
        }
        finally
        {
            foreach (var session in sessions)
                ToolHandlerUtilities.ClearSessionCache(session);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
        }
    }

    [TestMethod]
    public void FailedPromotion_RollsBackWithoutPublishingPartialSystemMessages()
    {
        var cache = new AssistantSessionContentCache();
        cache.RegisterDeclaringTypes([
            "Base",
            "Derived"], _ => throw new InvalidOperationException());
        try
        {
            cache.RegisterDeclaringTypes([
                "Base",
                "Derived"], _ => throw new InvalidOperationException());
            Assert.Fail("Expected a description failure.");
        }
        catch (InvalidOperationException) { }

        Assert.IsFalse(cache.IsTypePromoted("Base"));
        Assert.AreEqual(0, cache.GetSystemMessages().Count);
        cache.RegisterDeclaringTypes(
            [
                "Base",
                "Derived"],
            type => new(
                type,
                type,
                AssistantSystemMessageKind.TypeInfo,
                "complete",
                true));
        Assert.AreEqual(2, cache.GetSystemMessages().Count);
    }

    [TestMethod]
    public void HistorySerialization_DoesNotPersistRuntimeBindings()
    {
        var history = new AiAssistantHistoryDocument
        {
            LoadedExplanationKeys = ["explaining_generators"]
        };
        Assert.IsFalse(JObject.Parse(JsonConvert.SerializeObject(history)).ContainsKey("LoadedExplanationKeys"));
    }

    private static Task<ToolResponse> TypeInfo(Type type) => new GetTypeKindsInfoHandler().HandleAsync(
            new JObject
            {
                ["typeFullName"] = type.AssemblyQualifiedName
            },
            CancellationToken.None);

    private static string Compact(Type type) => AiAssistantTypeAliasHelper.CompressTypeName(type.FullName!);

    private static JObject[] Properties(JObject payload) => payload["kind"]?["properties"]?.Children<JObject>()
            .ToArray() ?? [];

    private static void AssertOmitted(
        JObject payload,
        Type declaringType,
        string property)
    {
        var omission = payload["kind"]?["omitted"]?.Children<JObject>()
            .SingleOrDefault(x => x.Value<string>("type") == Compact(declaringType));
        Assert.IsNotNull(omission, declaringType.Name + " must be grouped under omitted.");
        CollectionAssert.Contains(omission["properties"]!.Values<string>()
            .ToArray(), property);
        Assert.IsNull(omission["location"]);
    }

    private static Dictionary<string, JObject> SystemTypes(FlowBlox.Core.Models.FlowBlocks.AIRemote.Base.AIChatRequest request) =>
        request.SystemMessages.Skip(2)
            .Select(x => (JObject)JObject.Parse(x.Content)["kind"]!)
            .ToDictionary(x => x.Value<string>("fullName")!);

    private static JObject[] Results(AssistantMessagePair pair) => pair.ToolApiResponse.Split('\n')
            .Where(x => x.TrimStart().StartsWith("{", StringComparison.Ordinal))
            .Select(x => JObject.Parse(x)["response"]!["Result"]!)
            .SelectMany(x => x["batchResults"] is JArray batch ? batch.Select(y => (JObject)y["result"]!) : [(JObject)x])
            .ToArray();

    private static string Round(
        bool batch,
        params Type[] types)
    {
        var calls = new JArray(types.Select(type => new JObject
        {
            ["toolName"] = "GetTypeKindsInfo",
            ["arguments"] = new JObject
            {
                ["typeFullName"] = type.FullName
            }
        }));
        if (batch)
            calls = new JArray(new JObject
            {
                ["toolName"] = "BatchExecuteToolRequests",
                ["arguments"] = new JObject
                {
                    ["requests"] = calls
                }
            });

        return new JObject
        {
            ["assistantMessage"] = "Inspect",
            ["final"] = false,
            ["toolCalls"] = calls
        }.ToString(Formatting.None);
    }

    private class NestedBaseFixture : FlowBloxReactiveObject
    {
        public string BaseValue { get; set; } = "";
    }
    private sealed class NestedFixture : NestedBaseFixture
    {
        public string Value { get; set; } = "";
    }
    private sealed class ParentFixture : FlowBloxReactiveObject
    {
        public NestedFixture Child { get; set; } = new();
    }
}
