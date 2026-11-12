using FlowBlox.AIAssistant.Tools;
using FlowBlox.AIAssistant.Tools.Handler;
using FlowBlox.AIAssistant.Tools.State;
using FlowBlox.AIAssistant.Tools.Util;
using Newtonsoft.Json.Linq;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Generators;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Provider.Project;
using FlowBloxTest.FlowBlocks;

namespace FlowBloxTest.AIAssistant
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public sealed class TestExecutionInspectionTests : FlowBloxTestsBase
    {
        private const string Session = "test-execution-inspection";
        private static readonly string FullValue = new string('x', 20000) + " marker: actual full result beyond preview";

        [TestInitialize]
        public void Initialize()
        {
            ToolHandlerUtilities.SetCurrentSessionGuid(Session);
            Publish();
        }

        [TestCleanup]
        public void Cleanup()
        {
            ToolHandlerUtilities.ClearSessionCache(Session);
            ToolHandlerUtilities.SetCurrentSessionGuid(null);
        }

        private static AiAssistantTestExecutionSnapshot Publish(int executionCount = 1)
        {
            var snapshot = new AiAssistantTestExecutionSnapshot
            {
                Project = ToolHandlerUtilities.TryGetProject(), Source = "RunGenerationStrategies"
            };
            for (var i = 1; i <= executionCount; i++)
                snapshot.Executions.Add(new JObject
                {
                    ["executionId"] = i, ["testDefinitionName"] = "Example", ["phase"] = i == 1 ? "BeforeGeneration" : "AfterGeneration",
                    ["success"] = i > 1,
                    ["fieldValueAssignments"] = new JObject { ["Input::Content"] = FullValue },
                    ["testResults"] = new JObject
                    {
                        ["resultCount"] = 1,
                        ["rows"] = new JArray(new JObject
                        {
                            ["datasetIndex"] = 0,
                            ["values"] = new JArray(
                                new JObject { ["fieldName"] = "Content", ["fullyQualifiedFieldName"] = "A::Content", ["value"] = FullValue },
                                new JObject { ["fieldName"] = "Content", ["fullyQualifiedFieldName"] = "B::Content", ["value"] = "other" })
                        })
                    }
                });
            AiAssistantTestExecutionState.Set(Session, snapshot);
            return snapshot;
        }

        [TestMethod]
        public async Task Inspection_SearchesAssignmentsAndResultsAfterLimitedRetrieval()
        {
            var snapshot = Publish();
            var artifact = await new GetLatestTestExecutionArtifactHandler().HandleAsync(
                new JObject { ["executionId"] = 1 }, CancellationToken.None);
            Assert.IsTrue(artifact.Ok, artifact.Error);
            Assert.IsNotNull(artifact.Result["execution"]!["testResults"]!["rows"]![0]!["values"]![0]!["valueInfo"]);
            foreach (var args in new[]
            {
                new JObject { ["fullyQualifiedFieldName"] = "Input::Content" },
                new JObject { ["datasetIndex"] = 0, ["fullyQualifiedFieldName"] = "A::Content" }
            })
            {
                args["runId"] = snapshot.RunId;
                args["searchValues"] = "marker:";
                args["maxTokens"] = 20;
                var result = await new InspectExecutedTestFieldValueHandler().HandleAsync(args, CancellationToken.None);
                Assert.IsTrue(result.Ok, result.Error);
                StringAssert.StartsWith(result.Result.Value<string>("value"), "marker:");
            }

            var around = await new InspectExecutedTestFieldValueHandler().HandleAsync(new JObject
            {
                ["fullyQualifiedFieldName"] = "Input::Content", ["searchValues"] = "marker:",
                ["searchMode"] = "LookAround", ["maxTokens"] = 20
            }, CancellationToken.None);
            Assert.IsTrue(around.Ok, around.Error);
            StringAssert.Contains(around.Result.Value<string>("value"), "xxx");
            StringAssert.Contains(around.Result.Value<string>("value"), "marker:");
            var page = await new InspectExecutedTestFieldValueHandler().HandleAsync(new JObject
            {
                ["fullyQualifiedFieldName"] = "Input::Content", ["searchIndex"] = FullValue.Length - 7
            }, CancellationToken.None);
            Assert.AreEqual(FullValue.Substring(FullValue.Length - 7), page.Result.Value<string>("value"));
        }

        [TestMethod]
        public async Task Inspection_RejectsAmbiguousFieldsInvalidRowsAndMixedSelectors()
        {
            foreach (var args in new[]
            {
                new JObject { ["datasetIndex"] = 0, ["fieldName"] = "Content" },
                new JObject { ["datasetIndex"] = -1, ["fieldName"] = "Content" },
                new JObject { ["fullyQualifiedFieldName"] = "missing" },
                new JObject { ["fullyQualifiedFieldName"] = "Input::Content", ["datasetIndex"] = 0 },
                new JObject { ["fullyQualifiedFieldName"] = "Input::Content", ["searchMode"] = "invalid" },
                new JObject { ["fullyQualifiedFieldName"] = "Input::Content", ["maxTokens"] = int.MaxValue }
            })
                Assert.IsFalse((await new InspectExecutedTestFieldValueHandler().HandleAsync(args, CancellationToken.None)).Ok, args.ToString());
        }

        [TestMethod]
        public async Task RunHandlers_RetainFailedTestAndBothGenerationPhasesBeforeShutdown()
        {
            var previous = FlowBloxProjectManager.Instance.ActiveProject;
            try
            {
                FlowBloxProjectManager.Instance.ActiveProject = new FlowBloxProject();
                var start = CreateFlowBlock<StartFlowBlock>();
                var format = CreateFlowBlock<FormatFlowBlock>(start);
                format.FormatExpression = "before";
                var definition = CreateManagedObject<FlowBloxTestDefinition>();
                definition.Name = "Inspection integration";
                format.TestDefinitions.Add(definition);
                var config = definition.Entries.Single(x => x.FlowBlock == format)
                    .FlowBloxTestConfigurations.Single(x => x.FieldElement == format.ResultField);
                config.SelectionMode = FlowBloxTestConfigurationSelectionMode.UserInput_ExpectedValue;
                config.UserInput = "after";

                var test = await new RunTestDefinitionHandler().HandleAsync(new JObject
                {
                    ["testDefinitionName"] = definition.Name, ["targetFlowBlockName"] = format.Name
                }, CancellationToken.None);
                Assert.IsFalse(test.Ok);
                Assert.IsNotNull(test.Result["testExecutionArtifact"], test.Error);
                var failed = await new InspectExecutedTestFieldValueHandler().HandleAsync(new JObject
                {
                    ["datasetIndex"] = 0, ["fullyQualifiedFieldName"] = format.ResultField.FullyQualifiedName
                }, CancellationToken.None);
                Assert.IsTrue(failed.Ok, failed.Error);
                Assert.AreEqual("before", failed.Result.Value<string>("value"));

                format.GenerationStrategies.Add(new FixFormatStrategy(format));
                var generated = await new RunGenerationStrategiesHandler().HandleAsync(new JObject
                {
                    ["flowBlockName"] = format.Name
                }, CancellationToken.None);
                Assert.IsTrue(generated.Ok, generated.Error);
                var snapshot = AiAssistantTestExecutionState.Get(Session, ToolHandlerUtilities.TryGetProject())!;
                Assert.AreEqual(2, snapshot.Executions.Count);
                Assert.AreEqual("BeforeGeneration", snapshot.Executions[0].Value<string>("phase"));
                Assert.AreEqual("AfterGeneration", snapshot.Executions[1].Value<string>("phase"));
                Assert.AreEqual("before", snapshot.Executions[0]["testResults"]!["rows"]![0]!["values"]![0]!.Value<string>("value"));
                Assert.AreEqual("after", snapshot.Executions[1]["testResults"]!["rows"]![0]!["values"]![0]!.Value<string>("value"));
                Assert.AreEqual("after", snapshot.Executions[1]["fieldValueAssignments"]!.Value<string>(format.ResultField.FullyQualifiedName));
            }
            finally
            {
                FlowBloxProjectManager.Instance.ActiveProject = previous;
            }
        }

        private sealed class FixFormatStrategy : FlowBloxGenerationStrategyBase
        {
            public FixFormatStrategy(FormatFlowBlock source) : base(source) { }
            public override object Execute(BaseRuntime runtime, Dictionary<FlowBloxTestDefinition, FlowBloxTestResult> testResults) => "after";
            public override void Assign(object value) => ((FormatFlowBlock)Source).FormatExpression = (string)value;
            public override bool CanExecute(out Dictionary<FlowBloxTestDefinition, List<string>> testDefinitionToMessages, out List<string> messages)
            {
                testDefinitionToMessages = new();
                messages = new();
                return true;
            }
        }

        [TestMethod]
        public async Task MultipleExecutions_RequireSelectionAndRejectStaleRun()
        {
            var old = Publish();
            Publish(2);
            var list = await new GetLatestTestExecutionArtifactHandler().HandleAsync(new JObject(), CancellationToken.None);
            Assert.AreEqual(2, ((JArray)list.Result["executions"]!).Count);
            var handler = new InspectExecutedTestFieldValueHandler();
            var args = new JObject { ["fullyQualifiedFieldName"] = "Input::Content" };
            Assert.IsFalse((await handler.HandleAsync(args, CancellationToken.None)).Ok);
            args["executionId"] = 2;
            Assert.IsTrue((await handler.HandleAsync(args, CancellationToken.None)).Ok);
            args["runId"] = old.RunId;
            Assert.IsFalse((await handler.HandleAsync(args, CancellationToken.None)).Ok);
        }

        [TestMethod]
        public void Snapshots_AreIsolatedFromMutationOtherSessionsAndOtherProjects()
        {
            var original = Publish();
            original.Executions.Clear();
            var copy = AiAssistantTestExecutionState.Get(Session, original.Project)!;
            Assert.AreEqual(1, copy.Executions.Count);
            copy.Executions.Clear();
            Assert.AreEqual(1, AiAssistantTestExecutionState.Get(Session, original.Project)!.Executions.Count);
            Assert.IsNull(AiAssistantTestExecutionState.Get("other-session", original.Project));
            Assert.IsNull(AiAssistantTestExecutionState.Get(Session, new object()));
            ToolHandlerUtilities.ClearSessionCache(Session);
            Assert.IsNull(AiAssistantTestExecutionState.Get(Session, original.Project));
        }

    }
}
