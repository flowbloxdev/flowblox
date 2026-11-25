using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect executions and results from the latest test run.")]
    internal sealed class GetLatestTestExecutionArtifactHandler : ToolHandlerBase
    {
        public override string Name => "GetLatestTestExecutionArtifact";
        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(Name,
            "Lists executions from the latest RunTestDefinition or RunGenerationStrategies run; with executionId returns limited result datasets and assignments. Does not execute tests.",
            new JObject
            {
                ["runId"] = "string? (safety check; only latest run is retained per session/project)",
                ["executionId"] = "int? (omit to list executions; selects one test and generation phase)",
                ["datasetIndex"] = "int? (0-based; restrict result rows for the selected execution)",
                ["usageHint"] = "Use fullyQualifiedFieldName with InspectExecutedTestFieldValue for truncated values; add datasetIndex for a result value and omit it for an assignment."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var resolved = TestExecutionArtifactUtilities.TryResolve(args, out var snapshot, out var execution, out var error);
            if (snapshot != null && !args.ContainsKey("executionId") && !args.ContainsKey("datasetIndex") &&
                (string.IsNullOrWhiteSpace(args.Value<string>("runId")) ||
                 string.Equals(args.Value<string>("runId"), snapshot.RunId, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult(ToolHandlerUtilities.Ok(snapshot.Summary()));
            if (!resolved)
                return Task.FromResult(ToolHandlerUtilities.Fail(error));

            var index = args.Value<int?>("datasetIndex");
            if (index.HasValue && (index < 0 || index >= execution!["testResults"]!.Value<int>("resultCount")))
                return Task.FromResult(ToolHandlerUtilities.Fail("datasetIndex is out of range for this execution."));
            var limiter = FieldValueResponseLimiter.FromConfiguration();
            var payload = snapshot!.Summary();
            payload["execution"] = TestExecutionArtifactUtilities.Limit(execution!, limiter, index);
            payload["fieldValueOutput"] = limiter.CreateMetadata();
            return Task.FromResult(ToolHandlerUtilities.Ok(payload));
        }
    }
}
