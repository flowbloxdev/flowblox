using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;
using System.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    [ToolPurpose("Inspect the last debug run and result datasets.")]
    internal sealed class GetLastDebugArtefactHandler : ToolHandlerBase
    {
        public override AssistantScope Scope => AssistantScope.Project;

        public override string Name => "GetLastDebugArtefact";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Returns debug artefacts from the last debug run: field changes and generated result datasets.",
            new JObject
            {
                ["fieldChangeId"] = "int? (use this for FieldChange artefact retrieval)",
                ["generatedResultId"] = "int? (use this for GeneratedResult artefact retrieval)",
                ["datasetSelectionMode"] = "string? (First|Last|Index, only relevant for generatedResultId; default: First)",
                ["datasetIndex"] = "int? (0-based; when set, only this generated-result dataset is returned)",
                ["runId"] = "string? (optional safety check)",
                ["usageHint"] =
                    "Use fieldChangeId only for field-change artefacts. " +
                    "Use generatedResultId for generated-result artefacts. " + 
                    "Use InspectFieldValue for detailed navigation inside truncated or oversized field-value strings."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            var snapshot = AiAssistantDebugRunState.Get();
            if (snapshot == null)
                return Task.FromResult(ToolHandlerUtilities.Fail("No debug run available yet. Run 'RunProjectDebugTest' first."));

            var requestedRunId = (args.Value<string>("runId") ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(requestedRunId)
                && !string.Equals(requestedRunId, snapshot.RunId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    $"Requested runId '{requestedRunId}' does not match last run '{snapshot.RunId}'."));
            }

            var fieldChangeId = args.Value<int?>("fieldChangeId");
            var generatedResultId = args.Value<int?>("generatedResultId");
            var hasFieldChangeId = fieldChangeId.HasValue && fieldChangeId.Value > 0;
            var hasGeneratedResultId = generatedResultId.HasValue && generatedResultId.Value > 0;

            if (!hasFieldChangeId && !hasGeneratedResultId)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    "Provide either fieldChangeId or generatedResultId."));
            }

            if (hasFieldChangeId && hasGeneratedResultId)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(
                    "Provide only one artefact identifier: fieldChangeId or generatedResultId."));
            }

            if (hasFieldChangeId)
            {
                var fieldChanges = GetArrayIgnoreCase(snapshot.DebuggingResult, "FieldValueChanges", "fieldValueChanges");
                var requestedFieldChangeId = fieldChangeId.GetValueOrDefault();
                var fieldChange = fieldChanges
                    .OfType<JObject>()
                    .FirstOrDefault(x => GetIntIgnoreCase(x, "Id", "id") == requestedFieldChangeId);

                if (fieldChange == null)
                    return Task.FromResult(ToolHandlerUtilities.Fail($"FieldChangeId '{requestedFieldChangeId}' not found in last debug run."));

                var outputLimiter = FieldValueResponseLimiter.FromConfiguration();
                var limitedFieldChange = LimitFieldChange(fieldChange, outputLimiter);
                return Task.FromResult(ToolHandlerUtilities.Ok(new JObject
                {
                    ["runId"] = snapshot.RunId,
                    ["createdUtc"] = snapshot.CreatedUtc,
                    ["artefactType"] = "FieldChange",
                    ["fieldChange"] = limitedFieldChange,
                    ["fieldValueOutput"] = outputLimiter.CreateMetadata()
                }));
            }

            var generatedResults = GetArrayIgnoreCase(snapshot.DebuggingResult, "GeneratedResults", "generatedResults");
            var requestedGeneratedResultId = generatedResultId.GetValueOrDefault();
            var generatedResult = generatedResults
                .OfType<JObject>()
                .FirstOrDefault(x => GetIntIgnoreCase(x, "Id", "id") == requestedGeneratedResultId);

            if (generatedResult == null)
                return Task.FromResult(ToolHandlerUtilities.Fail($"GeneratedResultId '{requestedGeneratedResultId}' not found in last debug run."));

            var datasets = GetArrayIgnoreCase(generatedResult, "Datasets", "datasets");
            var selectionMode = (args.Value<string>("datasetSelectionMode") ?? "First").Trim();
            var datasetIndex = args.Value<int?>("datasetIndex");
            var selectedDatasetInfo = SelectDataset(datasets, selectionMode, datasetIndex);
            if (!selectedDatasetInfo.Ok)
                return Task.FromResult(ToolHandlerUtilities.Fail(selectedDatasetInfo.ErrorMessage));

            var requestedDatasetIndex = ValidateDatasetIndex(datasetIndex, datasets.Count);
            if (requestedDatasetIndex.ErrorMessage.Length > 0)
                return Task.FromResult(ToolHandlerUtilities.Fail(requestedDatasetIndex.ErrorMessage));

            var generatedResultOutputLimiter = FieldValueResponseLimiter.FromConfiguration();
            var limitedGeneratedResultInfo = LimitGeneratedResult(
                generatedResult,
                requestedDatasetIndex.Index,
                generatedResultOutputLimiter);

            var payload = new JObject
            {
                ["runId"] = snapshot.RunId,
                ["createdUtc"] = snapshot.CreatedUtc,
                ["artefactType"] = "GeneratedResult",
                ["generatedResult"] = limitedGeneratedResultInfo.Value,
                ["selectedDatasetIndex"] = selectedDatasetInfo.SelectedIndex,
                ["includedDatasetIndex"] = requestedDatasetIndex.Index.HasValue
                    ? new JValue(requestedDatasetIndex.Index.Value)
                    : JValue.CreateNull(),
                ["resultDatasetCount"] = limitedGeneratedResultInfo.TotalDatasetCount,
                ["includedDatasetCount"] = limitedGeneratedResultInfo.IncludedDatasetCount,
                ["resultDatasetsOmittedDueToLimit"] = limitedGeneratedResultInfo.LimitStoppedDatasetOutput,
                ["fieldValueOutput"] = generatedResultOutputLimiter.CreateMetadata()
            };

            if (limitedGeneratedResultInfo.LimitStoppedDatasetOutput)
            {
                payload["resultDatasetLimitMessage"] =
                    "Additional result datasets were omitted due to max field-value tokens per response.";
            }

            return Task.FromResult(ToolHandlerUtilities.Ok(payload));
        }

        private static JObject LimitFieldChange(JObject fieldChange, FieldValueResponseLimiter limiter)
        {
            var result = (JObject)fieldChange.DeepClone();
            LimitStringProperty(result, "OldValue", "oldValue", "oldValueInfo", limiter);
            LimitStringProperty(result, "NewValue", "newValue", "newValueInfo", limiter);
            return result;
        }

        private static LimitedGeneratedResult LimitGeneratedResult(
            JObject generatedResult,
            int? datasetIndex,
            FieldValueResponseLimiter limiter)
        {
            var result = (JObject)generatedResult.DeepClone();
            var datasets = GetArrayIgnoreCase(result, "Datasets", "datasets");
            var totalDatasetCount = datasets.Count;
            if (datasetIndex.HasValue)
            {
                var matchedDataset = datasets
                    .OfType<JObject>()
                    .FirstOrDefault(x => (GetIntIgnoreCase(x, "DatasetIndex", "datasetIndex") ?? -1) == datasetIndex.Value)
                    ?? datasets[datasetIndex.Value] as JObject;
                var filteredDatasets = matchedDataset == null
                    ? new JArray()
                    : new JArray(matchedDataset);
                SetArrayIgnoreCase(result, filteredDatasets, "Datasets", "datasets");
                datasets = filteredDatasets;
            }

            var limitedDatasets = new JArray();
            var stoppedDueToLimit = false;
            foreach (var dataset in datasets.OfType<JObject>())
            {
                if (limiter.RemainingTokens <= 0)
                {
                    stoppedDueToLimit = true;
                    break;
                }

                var limitedDataset = (JObject)dataset.DeepClone();
                limitedDatasets.Add(limitedDataset);
                var limitedMappings = GetArrayIgnoreCase(limitedDataset, "FieldValueMappings", "fieldValueMappings");
                foreach (var mapping in limitedMappings.OfType<JObject>())
                    LimitStringProperty(mapping, "Value", "value", "valueInfo", limiter);
            }

            SetArrayIgnoreCase(result, limitedDatasets, "Datasets", "datasets");
            return new LimitedGeneratedResult(result, totalDatasetCount, limitedDatasets.Count, stoppedDueToLimit);
        }

        private static void LimitStringProperty(
            JObject obj,
            string pascalName,
            string camelName,
            string infoName,
            FieldValueResponseLimiter limiter)
        {
            var property = GetPropertyIgnoreCase(obj, pascalName, camelName);
            if (property == null)
                return;

            var limited = limiter.Limit(property.Value.Value<string>() ?? string.Empty);
            property.Value = limited.Value;
            var metadata = limited.ToMetadata();
            if (metadata.HasValues)
                obj[infoName] = metadata;
        }

        private static DatasetSelectionResult SelectDataset(
            JArray datasets,
            string selectionModeRaw,
            int? datasetIndex)
        {
            var selectionMode = string.IsNullOrWhiteSpace(selectionModeRaw)
                ? "First"
                : selectionModeRaw.Trim();

            if (datasets == null || datasets.Count == 0)
            {
                return DatasetSelectionResult.Success(-1);
            }

            if (string.Equals(selectionMode, "First", StringComparison.OrdinalIgnoreCase))
            {
                return DatasetSelectionResult.Success(0);
            }

            if (string.Equals(selectionMode, "Last", StringComparison.OrdinalIgnoreCase))
            {
                var lastIndex = datasets.Count - 1;
                return DatasetSelectionResult.Success(lastIndex);
            }

            if (string.Equals(selectionMode, "Index", StringComparison.OrdinalIgnoreCase))
            {
                if (!datasetIndex.HasValue || datasetIndex.Value < 0)
                {
                    return DatasetSelectionResult.Fail("datasetIndex must be provided and >= 0 when datasetSelectionMode is 'Index'.");
                }

                if (datasetIndex.Value >= datasets.Count)
                {
                    return DatasetSelectionResult.Fail($"datasetIndex {datasetIndex.Value} is out of range. Available range: 0..{datasets.Count - 1}.");
                }

                return DatasetSelectionResult.Success(datasetIndex.Value);
            }

            return DatasetSelectionResult.Fail("datasetSelectionMode must be one of: First, Last, Index.");
        }

        private static JArray GetArrayIgnoreCase(JObject root, params string[] keys)
        {
            if (root == null)
                return new JArray();

            foreach (var key in keys)
            {
                if (root[key] is JArray direct)
                    return direct;
            }

            var prop = root.Properties()
                .FirstOrDefault(x => keys.Any(k => string.Equals(k, x.Name, StringComparison.OrdinalIgnoreCase)));

            return prop?.Value as JArray ?? new JArray();
        }

        private static void SetArrayIgnoreCase(JObject root, JArray value, params string[] keys)
        {
            var property = GetPropertyIgnoreCase(root, keys);
            if (property != null)
            {
                property.Value = value;
                return;
            }

            root[keys.First()] = value;
        }

        private static int? GetIntIgnoreCase(JObject root, params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = root.Value<int?>(key);
                if (value.HasValue)
                    return value;
            }

            var prop = root.Properties()
                .FirstOrDefault(x => keys.Any(k => string.Equals(k, x.Name, StringComparison.OrdinalIgnoreCase)));

            return prop?.Value?.Value<int?>();
        }

        private static JProperty? GetPropertyIgnoreCase(JObject root, params string[] keys)
        {
            if (root == null)
                return null;

            foreach (var key in keys)
            {
                var property = root.Property(key, StringComparison.OrdinalIgnoreCase);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static DatasetIndexValidationResult ValidateDatasetIndex(int? datasetIndex, int datasetCount)
        {
            if (!datasetIndex.HasValue)
                return DatasetIndexValidationResult.Success(null);

            if (datasetIndex.Value < 0 || datasetIndex.Value >= datasetCount)
                return DatasetIndexValidationResult.Fail($"datasetIndex {datasetIndex.Value} is out of range. Available range: 0..{Math.Max(0, datasetCount - 1)}.");

            return DatasetIndexValidationResult.Success(datasetIndex.Value);
        }

        private sealed record DatasetSelectionResult(bool Ok, string ErrorMessage, int SelectedIndex)
        {
            public static DatasetSelectionResult Success(int selectedIndex) => new(true, string.Empty, selectedIndex);
            public static DatasetSelectionResult Fail(string errorMessage) => new(false, errorMessage, -1);
        }

        private sealed record DatasetIndexValidationResult(int? Index, string ErrorMessage)
        {
            public static DatasetIndexValidationResult Success(int? index) => new(index, string.Empty);
            public static DatasetIndexValidationResult Fail(string errorMessage) => new(null, errorMessage);
        }

        private sealed record LimitedGeneratedResult(
            JObject Value,
            int TotalDatasetCount,
            int IncludedDatasetCount,
            bool LimitStoppedDatasetOutput);
    }
}
