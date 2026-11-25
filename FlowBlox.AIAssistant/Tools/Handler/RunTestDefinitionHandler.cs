using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Testing;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Execute a test definition and collect results.")]
    internal sealed class RunTestDefinitionHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "RunTestDefinition";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Executes a FlowBlox test definition and returns runtime protocol plus target test results and field assignments. " +
            "Retains full values for GetLatestTestExecutionArtifact and InspectExecutedTestFieldValue. Use only when test execution is requested or needed for generator/test debugging.",
            new JObject
            {
                ["testDefinitionName"] = "string",
                ["targetFlowBlockName"] = "string? (optional; if omitted, latest linked flow block is used)",
                ["synchronizeBeforeRun"] = "bool? (default: true)",
                ["maxProtocolEntries"] = "optional: int (default: 20). Returns the latest entries in chronological order.",
                ["usageHint"] = "Prefer RunGenerationStrategies first for generator work. Use this tool when test-definition execution or detailed test-result inspection is required."
            });

        public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                var testDefinitionName = (args.Value<string>("testDefinitionName") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(testDefinitionName))
                    return ToolHandlerUtilities.Fail("testDefinitionName is required.");

                var targetFlowBlockName = (args.Value<string>("targetFlowBlockName") ?? string.Empty).Trim();
                var synchronizeBeforeRun = args.Value<bool?>("synchronizeBeforeRun") ?? true;
                var maxProtocolEntries = Math.Max(
                    1,
                    args.Value<int?>("maxProtocolEntries")
                    ?? args.Value<int?>("maxProtocolPreviewEntries")
                    ?? 20);

                var registry = ToolHandlerUtilities.GetRegistry();
                var testDefinition = ToolHandlerUtilities.ResolveManagedObjectByName(registry, testDefinitionName) as FlowBloxTestDefinition;
                if (testDefinition == null)
                    return ToolHandlerUtilities.Fail($"Test definition '{testDefinitionName}' was not found.");

                BaseFlowBlock? targetFlowBlock = null;
                if (!string.IsNullOrWhiteSpace(targetFlowBlockName))
                {
                    targetFlowBlock = ToolHandlerUtilities.ResolveFlowBlockByName(registry, targetFlowBlockName);
                    if (targetFlowBlock == null)
                        return ToolHandlerUtilities.Fail($"Flow block '{targetFlowBlockName}' was not found.");
                }

                if (synchronizeBeforeRun)
                {
                    var synchronizer = new FlowBloxTestDefinitionSynchronizer();
                    synchronizer.Synchronize(testDefinition, targetFlowBlock);
                }

                var latestResolver = new FlowBloxTestDefinitionLatestFlowBlockResolver();
                var effectiveTargetFlowBlock = targetFlowBlock ?? latestResolver.ResolveLatestFlowBlock(testDefinition);
                if (effectiveTargetFlowBlock == null)
                    return ToolHandlerUtilities.Fail(
                        $"Test definition '{testDefinition.Name}' has no linked flow block. " +
                        "Provide targetFlowBlockName or link the test definition to a flow block first.");

                var includedFlowBlocks = testDefinition.Entries?
                    .Where(x => x.Execute)
                    .Select(x => x.FlowBlock)
                    .ExceptNull()
                    .Cast<BaseFlowBlock>()
                    .Distinct()
                    .ToList();

                var snapshot = new AiAssistantTestExecutionSnapshot
                {
                    Project = ToolHandlerUtilities.TryGetProject(),
                    Source = Name
                };
                AiAssistantTestExecutionState.Set(ToolHandlerUtilities.CurrentSessionGuid, snapshot);
                var protocolEntries = new List<JObject>();
                var testExecutor = new FlowBloxTestExecutor();
                FlowBloxTestResult? testResult = null;
                bool initialized = false;
                JObject? execution = null;

                void OnLogCreated(FlowBlox.Core.Models.Runtime.BaseRuntime runtime, string message, FlowBlox.Core.Enums.FlowBloxLogLevel logLevel)
                {
                    lock (protocolEntries)
                    {
                        protocolEntries.Add(new JObject
                        {
                            ["timestamp"] = DateTime.Now.ToString("O"),
                            ["status"] = logLevel.ToString(),
                            ["message"] = message ?? string.Empty
                        });
                    }
                }

                try
                {
                    ct.ThrowIfCancellationRequested();

                    testExecutor.Initialize(testDefinition, effectiveTargetFlowBlock, includedFlowBlocks);
                    initialized = true;

                    var runtime = testExecutor.GetRuntime();
                    runtime.LogMessageCreated += OnLogCreated;
                    try
                    {
                        testResult = await testExecutor.ExecuteTestAsync();
                        execution = TestExecutionArtifactUtilities.Capture(1, testDefinition.Name, effectiveTargetFlowBlock, testResult, "Test");
                        snapshot.Executions.Add(execution);
                        AiAssistantTestExecutionState.Set(ToolHandlerUtilities.CurrentSessionGuid, snapshot);
                    }
                    finally
                    {
                        runtime.LogMessageCreated -= OnLogCreated;
                    }
                }
                finally
                {
                    if (initialized)
                    {
                        try
                        {
                            testExecutor.Shutdown();
                        }
                        catch (Exception ex)
                        {
                            FlowBloxLogManager.Instance.GetLogger().Error(
                                "Failed to shut down the FlowBlox test executor after test execution.",
                                ex);
                        }
                    }
                }

                ct.ThrowIfCancellationRequested();

                var protocolEntryCountTotal = protocolEntries.Count;
                var protocol = ProtocolEntryWindowing.TakeLastInOriginalOrder(protocolEntries, maxProtocolEntries);
                var fieldValueLimiter = FieldValueResponseLimiter.FromConfiguration();
                var limitedExecution = TestExecutionArtifactUtilities.Limit(execution!, fieldValueLimiter);

                var payload = new JObject
                {
                    ["testDefinitionName"] = testDefinition.Name,
                    ["targetFlowBlockName"] = effectiveTargetFlowBlock.Name,
                    ["synchronizedBeforeRun"] = synchronizeBeforeRun,
                    ["success"] = testResult?.Success ?? false,
                    ["protocolEntryCount"] = protocol.Count,
                    ["protocolEntryCountTotal"] = protocolEntryCountTotal,
                    ["maxProtocolEntries"] = maxProtocolEntries,
                    ["protocol"] = protocol,
                    ["testExecutionArtifact"] = snapshot.Summary(),
                    ["testResults"] = limitedExecution["testResults"],
                    ["fieldValueAssignments"] = limitedExecution["fieldValueAssignments"],
                    ["fieldValueOutput"] = fieldValueLimiter.CreateMetadata()
                };

                if (limitedExecution["fieldValueAssignmentInfo"] is JObject assignmentInfo)
                    payload["fieldValueAssignmentInfo"] = assignmentInfo;

                if (testResult?.Success == true)
                    return ToolHandlerUtilities.Ok(payload);

                return ToolHandlerUtilities.Fail("Test definition execution failed.", payload);
            }
            catch (OperationCanceledException)
            {
                return ToolHandlerUtilities.Fail("Test definition execution was cancelled.");
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error(
                    "Failed to execute a FlowBlox test definition through the AI Assistant.",
                    ex);
                return ToolHandlerUtilities.Fail(ex.Message);
            }
        }
    }
}
