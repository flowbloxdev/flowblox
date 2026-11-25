using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect a retained external project output value.")]
    internal sealed class InspectOutputFieldValueHandler : ToolHandlerBase
    {
        public override string Name => "InspectOutputFieldValue";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Inspects one output field value from the last ExecuteExternalProject run.",
            new JObject
            {
                ["outputName"] = "string (ProjectOutputFlowBlock name)",
                ["datasetIndex"] = "int (0-based)",
                ["fieldName"] = "string",
                ["searchIndex"] = "int? (character index; default: 0)",
                ["searchValues"] = "string? (comma-separated markers; earliest match replaces searchIndex)",
                ["searchMode"] = "string? (StartAt|LookAround; default: StartAt)",
                ["maxTokens"] = "optional: int (default: configured MaxFieldValuesTokensPerResponse, normally 4000; maximum: that configured value). Use only as many tokens as needed.",
                ["runId"] = "string? (optional safety check)"
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = AiAssistantProjectRunState.Get(ToolHandlerUtilities.CurrentSessionGuid);
            if (snapshot == null)
                return Task.FromResult(ToolHandlerUtilities.Fail("No ExecuteExternalProject run is available in this session."));

            var requestedRunId = (args.Value<string>("runId") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(requestedRunId) &&
                !string.Equals(requestedRunId, snapshot.RunId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Requested runId '{requestedRunId}' does not match last ExecuteExternalProject run '{snapshot.RunId}'."));
            }

            var outputName = (args.Value<string>("outputName") ?? string.Empty).Trim();
            var datasetIndex = args.Value<int?>("datasetIndex");
            var fieldName = (args.Value<string>("fieldName") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(outputName))
                return Task.FromResult(ToolHandlerUtilities.Fail("outputName is required."));
            if (!datasetIndex.HasValue || datasetIndex.Value < 0)
                return Task.FromResult(ToolHandlerUtilities.Fail("datasetIndex is required and must be >= 0."));
            if (string.IsNullOrWhiteSpace(fieldName))
                return Task.FromResult(ToolHandlerUtilities.Fail("fieldName is required."));

            var outputProperty = snapshot.Outputs.Property(outputName, StringComparison.OrdinalIgnoreCase);
            if (outputProperty?.Value is not JArray datasets)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Output '{outputName}' was not found in the last ExecuteExternalProject run."));
            if (datasetIndex.Value >= datasets.Count || datasets[datasetIndex.Value] is not JObject dataset)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"datasetIndex {datasetIndex.Value} is out of range. Available dataset count: {datasets.Count}."));
            }

            var values = dataset["values"] as JObject ?? new JObject();
            var valueProperty = values.Property(fieldName, StringComparison.OrdinalIgnoreCase);
            if (valueProperty == null)
                return Task.FromResult(ToolHandlerUtilities.Fail($"Output field '{fieldName}' was not found in '{outputName}'."));
            if (!FieldValueInspectionToolUtilities.TryCreateOptions(args, out var options, out var error))
                return Task.FromResult(ToolHandlerUtilities.Fail(error));
            if (!FieldValueResponseLimiter.TryFromConfiguration(
                    args.Value<int?>("maxTokens"),
                    out var limiter,
                    out var limiterError))
                return Task.FromResult(ToolHandlerUtilities.Fail(limiterError));

            var value = valueProperty.Value.Type == JTokenType.String
                ? valueProperty.Value.Value<string>() ?? string.Empty
                : valueProperty.Value.ToString(Newtonsoft.Json.Formatting.None);
            var inspected = limiter!.Inspect(value, options);

            return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
            {
                ["runId"] = snapshot.RunId,
                ["createdUtc"] = snapshot.CreatedUtc,
                ["projectName"] = snapshot.ProjectName,
                ["outputName"] = outputProperty.Name,
                ["datasetIndex"] = datasetIndex.Value,
                ["fieldName"] = valueProperty.Name,
                ["value"] = inspected.Value,
                ["valueInfo"] = inspected.ToMetadata(),
                ["fieldValueOutput"] = limiter.CreateMetadata()
            }));
        }
    }
}
