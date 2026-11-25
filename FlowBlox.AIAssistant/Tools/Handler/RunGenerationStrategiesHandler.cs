using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.Base;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Execute generation strategies for a FlowBlock.")]
    internal sealed class RunGenerationStrategiesHandler : ToolHandlerBase
    {
        public override string Name => "RunGenerationStrategies";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Executes generation strategies for a specific flow block and returns protocol logs (status/message). " +
            "Retains internal test executions for GetLatestTestExecutionArtifact and InspectExecutedTestFieldValue.",
            new JObject
            {
                ["flowBlockName"] = "string",
                ["maxProtocolEntries"] = "optional: int (default: 20). Returns the latest entries in chronological order.",
                ["usageHint"] = "After generator configuration on a flow block, run this tool to generate values. Verify configuration via GetComponentSnapshot; inspect test values via GetLatestTestExecutionArtifact, then InspectExecutedTestFieldValue."
            });

        public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                var flowBlockName = (args.Value<string>("flowBlockName") ?? string.Empty).Trim();
                var maxProtocolEntries = Math.Max(1, args.Value<int?>("maxProtocolEntries") ?? 20);
                if (string.IsNullOrWhiteSpace(flowBlockName))
                {
                    return ToolHandlerUtilities.Fail("flowBlockName is required.");
                }

                var registry = ToolHandlerUtilities.GetRegistry();
                var flowBlock = ToolHandlerUtilities.ResolveFlowBlockByName(registry, flowBlockName);
                if (flowBlock == null)
                {
                    return ToolHandlerUtilities.Fail($"Flow block '{flowBlockName}' was not found.");
                }

                if (flowBlock.GenerationStrategies == null || flowBlock.GenerationStrategies.Count == 0)
                {
                    return ToolHandlerUtilities.Fail(
                        $"Flow block '{flowBlock.Name}' has no generation strategies configured.");
                }

                ct.ThrowIfCancellationRequested();

                var snapshot = new AiAssistantTestExecutionSnapshot
                {
                    Project = ToolHandlerUtilities.TryGetProject(),
                    Source = Name
                };
                AiAssistantTestExecutionState.Set(ToolHandlerUtilities.CurrentSessionGuid, snapshot);
                var protocolEntries = new List<JObject>();
                var generationExecutor = new FlowBlockGenerationStrategyExecutor(flowBlock);

                void OnLogCreated(object? sender, LogCreatedEventArgs eventArgs)
                {
                    lock (protocolEntries)
                    {
                        protocolEntries.Add(new JObject
                        {
                            ["timestamp"] = DateTime.Now.ToString("O"),
                            ["status"] = eventArgs.LogLevel.ToString(),
                            ["message"] = eventArgs.Message ?? string.Empty
                        });
                    }
                }

                void OnTestExecuted(FlowBlox.Core.Models.Testing.FlowBloxTestDefinition definition,
                    FlowBlox.Core.Models.Testing.FlowBloxTestResult result, string phase)
                {
                    snapshot.Executions.Add(TestExecutionArtifactUtilities.Capture(
                        snapshot.Executions.Count + 1, definition.Name, flowBlock, result, phase));
                    AiAssistantTestExecutionState.Set(ToolHandlerUtilities.CurrentSessionGuid, snapshot);
                }

                generationExecutor.TestExecuted += OnTestExecuted;
                generationExecutor.LogCreated += OnLogCreated;
                bool success;
                try
                {
                    success = await generationExecutor.ExecuteGenerationAsync();
                }
                finally
                {
                    generationExecutor.LogCreated -= OnLogCreated;
                    generationExecutor.TestExecuted -= OnTestExecuted;
                }

                ct.ThrowIfCancellationRequested();

                var protocolEntryCountTotal = protocolEntries.Count;
                var protocol = ProtocolEntryWindowing.TakeLastInOriginalOrder(protocolEntries, maxProtocolEntries);
                var payload = new JObject
                {
                    ["flowBlockName"] = flowBlock.Name,
                    ["testExecutionArtifact"] = snapshot.Summary(),
                    ["generationStrategyCount"] = flowBlock.GenerationStrategies.Count,
                    ["success"] = success,
                    ["protocolEntryCount"] = protocol.Count,
                    ["protocolEntryCountTotal"] = protocolEntryCountTotal,
                    ["maxProtocolEntries"] = maxProtocolEntries,
                    ["protocol"] = protocol
                };

                if (success)
                {
                    return ToolHandlerUtilities.Ok(payload);
                }

                return ToolHandlerUtilities.Fail("Generation strategies execution failed.", payload);
            }
            catch (OperationCanceledException)
            {
                return ToolHandlerUtilities.Fail("Generation strategies execution was cancelled.");
            }
            catch (Exception ex)
            {
                return ToolHandlerUtilities.Fail(ex.Message);
            }
        }
    }
}
