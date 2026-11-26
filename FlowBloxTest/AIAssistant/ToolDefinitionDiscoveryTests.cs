using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.Util;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlowBloxTest.AIAssistant;

[TestClass]
[DoNotParallelize]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public sealed class ToolDefinitionDiscoveryTests
{
    private const string FinalResponse = """{"assistantMessage":"Done","final":true,"toolCalls":[]}""";

    [TestMethod]
    public async Task SecondRetrievalAfterSummary_PromotesContractBeforeNextCallAndPreservesItAcrossRestore()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var definition = await Discover(new DefaultToolApi(), "GetTypeKindsInfo");
        var history = HistoryWithContract(definition);
        var contract = definition.RetainedContents.Single().Content;
        var call = new JObject
        {
            ["assistantMessage"] = "Inspect type",
            ["final"] = false,
            ["toolCalls"] = new JArray
            {
                new JObject
                {
                    ["toolName"] = "GetTypeKindsInfo",
                    ["arguments"] = new JObject { ["typeFullName"] = typeof(WebBrowserFlowBlock).FullName }
                }
            }
        }.ToString(Formatting.None);
        var executor = new QueuedAiExecutor(DiscoveryRequest(), call, FinalResponse);
        var service = CreateService(executor);
        service.RestoreSession(history);
        try
        {
            var result = await service.ExecutePromptAsync("Inspect after summary", CancellationToken.None);
            Assert.IsTrue(result.Success, string.Join(Environment.NewLine, result.Errors));
            Assert.IsTrue(executor.Requests.Any(request => request.Source == "FlowBloxAIAssistantSummary"));
            Assert.IsFalse(executor.ChatRequests[0].SystemMessages.Any(message => message.Content == contract));
            Assert.IsTrue(executor.ChatRequests[1].SystemMessages.Any(message => message.Content == contract));
            var saved = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(saved);
            Assert.AreEqual(contract, saved.RetainedContents.Single().Content);
            Assert.IsTrue(saved.SessionMessages.SelectMany(message => message.RetainedContents)
                .Any(content => content.Kind == AssistantSystemMessageKind.TypeInfo));
            var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(saved))!;
            var nextExecutor = new QueuedAiExecutor(FinalResponse);
            var nextService = CreateService(nextExecutor);
            try
            {
                nextService.RestoreSession(restored);
                await nextService.ExecutePromptAsync("Continue", CancellationToken.None);
                Assert.IsTrue(nextExecutor.ChatRequests[0].SystemMessages.Any(message => message.Content == contract));
            }
            finally
            {
                nextService.ResetSession();
            }
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    [DataRow(false, "failed")]
    [DataRow(true, " ")]
    public async Task FailedOrEmptySummary_LeavesContractPendingAndDoesNotChangeSystemPrefix(bool success, string summary)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var response = await Discover(new DefaultToolApi(), "GetTypeKindsInfo");
        var executor = new SummaryResultExecutor(success, summary, FinalResponse);
        var service = CreateService(executor);
        service.RestoreSession(HistoryWithContract(response));
        try
        {
            await service.ExecutePromptAsync("Continue after failed summary", CancellationToken.None);
            var saved = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(saved);
            Assert.AreEqual(0, saved.SummarizedMessageCount);
            Assert.AreEqual(0, saved.RetainedContents.Count);
            Assert.AreEqual(response.RetainedContents.Single().Content,
                saved.SessionMessages.SelectMany(message => message.RetainedContents).Single().Content);
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    public async Task CatalogDiscovery_ReturnsEveryExistingContractWithoutInformationLoss()
    {
        var api = new DefaultToolApi();
        var expected = api.GetToolDefinitions();
        var bootstrap = AssistantPromptBuilder.BuildSessionBootstrapPrompt(expected);
        var catalogLines = bootstrap.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        foreach (var definition in expected)
        {
            var rendered = catalogLines.Single(line => line.StartsWith("- " + definition.Name + ": ", StringComparison.Ordinal));
            var expectedLine = "- " + definition.Name + ": ";
            if (definition.IncludeContractInBootstrap)
            {
                expectedLine += definition.Description;
                if (definition.ArgumentsSchema.HasValues)
                    expectedLine += " args=" + definition.ArgumentsSchema.ToString(Formatting.None);
            }
            else
            {
                expectedLine += definition.ShortDescription;
            }
            Assert.AreEqual(expectedLine, rendered);
        }
        var response = await Execute(api, "GetToolDefinitions", new JObject
        {
            ["toolNames"] = new JArray(expected.Select(definition => definition.Name))
        });
        Assert.IsTrue(response.Ok, response.Error);
        var contracts = (JArray)response.Result!["definitions"]!;
        Assert.AreEqual(expected.Count, contracts.Count);
        foreach (var definition in expected)
        {
            var actual = contracts.Single(contract => contract.Value<string>("name") == definition.Name);
            Assert.AreEqual(definition.Description, actual.Value<string>("description"));
            Assert.IsTrue(JToken.DeepEquals(definition.ArgumentsSchema, actual["argumentsSchema"]), definition.Name);
        }
        contracts[0]!["argumentsSchema"]!["fixture"] = true;
        Assert.IsNull(expected[0].ArgumentsSchema["fixture"]);
    }

    [TestMethod]
    [DataRow(false, "failed")]
    [DataRow(true, " ")]
    public async Task SecondRetrieval_KeepsContractPendingWhenSummaryFailsOrIsEmpty(bool success, string summary)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var response = await Discover(new DefaultToolApi(), "GetTypeKindsInfo");
        var executor = new SummaryResultExecutor(success, summary, DiscoveryRequest(), FinalResponse);
        var service = CreateService(executor);
        service.RestoreSession(HistoryWithContract(response));
        try
        {
            await service.ExecutePromptAsync("Retrieve the contract again", CancellationToken.None);
            var saved = new AiAssistantHistoryDocument();
            service.UpdateHistorySessionMetadata(saved);
            Assert.AreEqual(0, saved.SummarizedMessageCount);
            Assert.AreEqual(0, saved.RetainedContents.Count);
            Assert.AreEqual("history", AssistantHistoryRetention.FindLocation(
                saved.SessionMessages, saved.RetainedContents, response.RetainedContents.Single().Key));
        }
        finally
        {
            service.ResetSession();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    [DataRow(AssistantSystemMessageKind.Explanation, 1)]
    [DataRow(AssistantSystemMessageKind.TypeInfo, 2)]
    [DataRow(AssistantSystemMessageKind.ToolDefinition, 2)]
    public void VerbatimContextRetention_AppliesSharedPolicyAcrossRestoreAndKeepsOneExactPayload(
        AssistantSystemMessageKind kind,
        int requiredRetrievals)
    {
        var response = new ToolResponse
        {
            Ok = true,
            Result = new JObject { ["data"] = "Exact fixture payload" },
            RetainedContents = [new AssistantRetainedContent
            {
                Key = "fixture:static-content",
                Content = "Exact fixture payload",
                Kind = kind
            }]
        };
        var history = new AiAssistantHistoryDocument { SessionMessages = [Pair(response)] };
        AssistantHistoryRetention.Promote(history.SessionMessages, history.RetainedContents);
        Assert.AreEqual(requiredRetrievals == 1 ? 1 : 0, history.RetainedContents.Count);
        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(history))!;
        var repeated = Pair(response);
        repeated.RetainedContents.Single().Content = string.Empty;
        restored.SessionMessages.Add(repeated);
        AssistantHistoryRetention.Promote(restored.SessionMessages, restored.RetainedContents);
        var retained = restored.RetainedContents.Single();
        Assert.AreEqual("Exact fixture payload", retained.Content);
        Assert.AreEqual(kind, retained.Kind);
        restored.SessionMessages.Add(Pair(response));
        AssistantHistoryRetention.Promote(restored.SessionMessages, restored.RetainedContents);
        Assert.AreEqual(1, restored.RetainedContents.Count);
        Assert.AreEqual("Exact fixture payload", restored.RetainedContents.Single().Content);
    }

    [TestMethod]
    public async Task ComponentDiscovery_RejectsWholeRequestWhenAnyToolIsOutsideScope()
    {
        var project = new FlowBloxProject();
        var component = new WebBrowserFlowBlock();
        var api = new DefaultToolApi
        {
            ComponentContext = new AssistantComponentContext(component, project.FlowBloxRegistry, () => true, item => item)
        };
        var response = await Discover(api, "GetTypeKindsInfo", "CreateOrUpdateSkillProject");
        Assert.IsFalse(response.Ok);
        Assert.AreEqual(0, response.RetainedContents.Count);
        var allowed = await Discover(api, "GetTypeKindsInfo", "GetSkillProjects", "GetDistributedDataContent");
        Assert.IsTrue(allowed.Ok, allowed.Error);
        Assert.AreEqual(3, ((JArray)allowed.Result!["definitions"]!).Count);
    }

    [TestMethod]
    public async Task RepeatedDiscovery_ReferencesHistoryThenSystemWithoutDuplicatingContracts()
    {
        var api = new DefaultToolApi();
        var messages = new List<AssistantSessionMessage>();
        var retained = new List<AssistantRetainedContent>();
        using var scope = ToolHandlerUtilities.BeginRetainedContentScope(
            key => AssistantHistoryRetention.FindLocation(messages, retained, key));
        var first = await Discover(api, "gettypekindsinfo", "GetTypeKindsInfo");
        messages.Add(Pair(first));
        AssistantHistoryRetention.Promote(messages, retained);
        Assert.AreEqual(0, retained.Count);
        var repeated = await Discover(api, "GetTypeKindsInfo");
        Assert.AreEqual(1, first.RetainedContents.Count);
        Assert.IsNull(repeated.Result!["definitions"]![0]!["argumentsSchema"]);
        Assert.IsTrue(repeated.RetainedContents.All(content => content.Content.Length == 0));
        messages.Add(Pair(repeated));
        AssistantHistoryRetention.Promote(messages, retained);
        Assert.AreEqual(first.RetainedContents.Single().Content, retained.Single().Content);
        var systemReference = await Discover(api, "GetTypeKindsInfo");
        Assert.AreEqual("The full tool contract is already available as a system message.",
            systemReference.Result!["definitions"]![0]!.Value<string>("message"));
    }

    [TestMethod]
    public async Task SecondRetrieval_PreservesNestedBatchContractsAndUnrelatedFailureAfterRestore()
    {
        var api = new DefaultToolApi();
        using var scope = ToolHandlerUtilities.BeginRetainedContentScope(_ => null);
        var response = await Execute(api, "BatchExecuteToolRequests", new JObject
        {
            ["continueOnError"] = true,
            ["requests"] = new JArray
            {
                new JObject
                {
                    ["toolName"] = "GetToolDefinitions",
                    ["arguments"] = new JObject { ["toolNames"] = new JArray("GetTypeKindsInfo", "GetSupportedTypes") }
                },
                new JObject
                {
                    ["toolName"] = "GetToolDefinitions",
                    ["arguments"] = new JObject { ["toolNames"] = new JArray("GetTypeKindsInfo", "GetSupportedTypes") }
                },
                new JObject { ["toolName"] = "UnknownFixtureTool", ["arguments"] = new JObject() }
            }
        });
        var original = Pair(response);
        var restored = JsonConvert.DeserializeObject<AiAssistantHistoryDocument>(JsonConvert.SerializeObject(
            new AiAssistantHistoryDocument { SessionMessages = [original] }))!;
        var retained = restored.RetainedContents;
        var messages = restored.SessionMessages;
        AssistantHistoryRetention.Promote(messages, retained);
        Assert.AreEqual(2, retained.Count);
        CollectionAssert.AreEqual(original.RetainedContents.Where(content => content.Content.Length > 0).Select(content => content.Content).ToArray(),
            retained.Select(content => content.Content).ToArray());
        var projected = JObject.Parse(((AssistantMessagePair)messages.Single()).ToolApiResponse);
        var batch = projected["response"]!["Result"]!["batchResults"]!;
        Assert.AreEqual(2, batch[0]!["result"]!["definitions"]!.Count());
        Assert.IsNotNull(batch[0]!["result"]!["definitions"]![1]!["argumentsSchema"]);
        Assert.AreEqual(original.ToolApiResponse, ((AssistantMessagePair)messages.Single()).ToolApiResponse);
        Assert.IsNotNull(batch[1]!["result"]!["definitions"]![0]!["retainedContentKey"]);
        Assert.IsFalse(batch[2]!.Value<bool>("ok"));
        Assert.IsTrue(batch[2]!.Value<string>("error")!.Contains("UnknownFixtureTool", StringComparison.Ordinal));
        Assert.IsNotNull(JObject.Parse(original.ToolApiResponse)["response"]!["Result"]!["batchResults"]![0]!["result"]!["definitions"]![0]!["argumentsSchema"]);
    }

    [TestMethod]
    public async Task ContractGuard_RejectsGuessedBatchCallsEvenAfterDiscoveryInSameResponse()
    {
        var api = new DefaultToolApi();
        using var scope = ToolHandlerUtilities.BeginRetainedContentScope(_ => null, requireToolDefinitions: true);
        var response = await Execute(api, "BatchExecuteToolRequests", new JObject
        {
            ["requests"] = new JArray
            {
                new JObject
                {
                    ["toolName"] = "GetToolDefinitions",
                    ["arguments"] = new JObject { ["toolNames"] = new JArray("GetToolboxEntries") }
                },
                new JObject
                {
                    ["toolName"] = "GetToolboxEntries",
                    ["arguments"] = new JObject { ["toolboxCategory"] = "FixtureCategory" }
                }
            }
        });
        Assert.IsFalse(response.Ok);
        var batch = response.Result!["batchResults"]!;
        Assert.IsTrue(batch[0]!.Value<bool>("ok"));
        Assert.IsFalse(batch[1]!.Value<bool>("ok"));
        StringAssert.Contains(batch[1]!.Value<string>("error"), "GetToolDefinitions");
    }

    [TestMethod]
    public async Task ContractGuard_AllowsRetainedContractAndRejectsItWhenNoLongerVisible()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        try
        {
            var api = new DefaultToolApi();
            var response = await Discover(api, "GetInputFiles");
            var key = response.RetainedContents.Single().Key;
            var visible = true;
            using var scope = ToolHandlerUtilities.BeginRetainedContentScope(
                requested => visible && requested == key ? "system" : null, requireToolDefinitions: true);
            var arguments = new JObject();
            Assert.IsTrue((await Execute(api, "GetInputFiles", arguments)).Ok);
            visible = false;
            Assert.IsFalse((await Execute(api, "GetInputFiles", arguments)).Ok);
            Assert.IsNotNull((await Discover(api, "GetInputFiles")).Result!["definitions"]![0]!["argumentsSchema"]);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    public async Task CoreEditingBatch_ExecutesWithoutContractDiscovery()
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        try
        {
            var block = project.FlowBloxRegistry.CreateFlowBlockUnregistered<WebBrowserFlowBlock>();
            project.FlowBloxRegistry.Register(block);
            var api = new DefaultToolApi();
            using var scope = ToolHandlerUtilities.BeginRetainedContentScope(_ => null, requireToolDefinitions: true);
            var response = await Execute(api, "BatchExecuteToolRequests", new JObject
            {
                ["requests"] = new JArray
                {
                    new JObject
                    {
                        ["toolName"] = "GetTypeKindsInfo",
                        ["arguments"] = new JObject { ["typeFullName"] = typeof(WebBrowserFlowBlock).FullName }
                    },
                    new JObject
                    {
                        ["toolName"] = "UpdateFlowBlock",
                        ["arguments"] = new JObject
                        {
                            ["name"] = block.Name,
                            ["path"] = "/Url",
                            ["value"] = "https://example.test/updated"
                        }
                    }
                }
            });

            Assert.IsTrue(response.Ok, response.Error);
            Assert.AreEqual("https://example.test/updated", block.Url);
        }
        finally
        {
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    [TestMethod]
    public async Task ContractGuard_RejectsExecutionBeforeRequestingUserApproval()
    {
        var approvalRequests = 0;
        var api = new DefaultToolApi
        {
            ToolExecutionConfirmationCallback = _ =>
            {
                approvalRequests++;
                return false;
            }
        };
        using var scope = ToolHandlerUtilities.BeginRetainedContentScope(_ => null, requireToolDefinitions: true);
        var response = await Execute(api, "ExecuteInputFileCommand", new JObject { ["key"] = "fixture.txt" });
        Assert.IsFalse(response.Ok);
        Assert.AreEqual(0, approvalRequests);
        Assert.IsTrue(response.Error!.Contains("GetToolDefinitions", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InvalidDiscovery_DoesNotRegisterPartialContracts()
    {
        var api = new DefaultToolApi();
        using var scope = ToolHandlerUtilities.BeginRetainedContentScope(_ => null);
        var response = await Discover(api, "GetTypeKindsInfo", "UnknownFixtureTool");
        Assert.IsFalse(response.Ok);
        Assert.IsNull(ToolHandlerUtilities.GetRetainedContentLocation(GetToolDefinitionsHandler.GetRetentionKey("GetTypeKindsInfo")));
    }

    private static AssistantMessagePair Pair(ToolResponse response)
    {
        return new AssistantMessagePair
        {
            AssistantRequest = "Inspect tool contracts",
            ToolApiResponse = new JObject { ["response"] = JToken.FromObject(response) }.ToString(Formatting.None),
            RetainedContents = response.RetainedContents.Select(content =>
            {
                var copy = content.Clone();
                copy.ResultIndex = 0;
                return copy;
            }).ToList()
        };
    }

    private static AiAssistantHistoryDocument HistoryWithContract(ToolResponse response) => new()
    {
        SessionMessages =
        [
            new AssistantSingleMessage { MessageRole = "user", Message = "Inspect metadata" },
            Pair(response),
            new AssistantSingleMessage { MessageRole = "assistant", Message = "Contract inspected" }
        ]
    };

    private static AiAssistantService CreateService(IAiExecutor executor)
    {
        FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
        var configuration = new AssistantConfiguration
        {
            MaxToolRounds = 6,
            MaxLatestMessages = 1,
            MinLatestMessages = 1,
            MaxContextTokens = 1000000,
            MaxSummaryTokens = 100000,
            SummaryCompactionRate = 1d,
            EnableAutomaticAdjustment = false,
            AttachProjectJsonAutomatically = false
        };
        return new AiAssistantService(executor, new DefaultToolApi(), configurationProvider: () => configuration);
    }

    private static Task<ToolResponse> Discover(DefaultToolApi api, params string[] names) =>
        Execute(api, "GetToolDefinitions", new JObject { ["toolNames"] = new JArray(names) });

    private static string DiscoveryRequest() => new JObject
    {
        ["assistantMessage"] = "Read tool contract",
        ["final"] = false,
        ["toolCalls"] = new JArray
        {
            new JObject
            {
                ["toolName"] = "GetToolDefinitions",
                ["arguments"] = new JObject { ["toolNames"] = new JArray("GetTypeKindsInfo") }
            }
        }
    }.ToString(Formatting.None);

    private static Task<ToolResponse> Execute(DefaultToolApi api, string name, JObject arguments) =>
        api.ExecuteAsync(new ToolRequest { ToolName = name, Arguments = arguments }, CancellationToken.None);
}
