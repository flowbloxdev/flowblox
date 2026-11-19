using System.Text.Json;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class ExecuteExternalProjectHandler : ToolHandlerBase
    {
        public override string Name => "ExecuteExternalProject";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Executes a saved project or Project Space GUID and returns its ProjectOutputFlowBlock values. Provide a reason; FlowBlox requests approval. Local approvals may be remembered. Use InspectOutputFieldValue for truncated values.",
            new JObject
            {
                ["projectFile"] = "string? (saved .fbprj path; alternative to projectSpaceGuid)",
                ["projectSpaceGuid"] = "string? (instead of projectFile)",
                ["projectSpaceVersion"] = "int? (stable version; default latest stable, or current if none)",
                ["private"] = "bool? (private PS access; isolated execution requires Remember credentials; default false)",
                ["reason"] = "string (why this execution is required)",
                ["inputParameters"] = "object? (Input field name to scalar/text value; supports FlowBlox placeholders; omitted or empty inputs use the field's defaultValue)",
                ["outputFields"] = "string[]? (optional ProjectOutputFlowBlock property-name filter)"
            });

        public override async Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                if (ToolHandlerUtilities.IsSkillProjectActive())
                    return ToolHandlerUtilities.Fail("Save and close the active skill project before executing a saved project.");

                var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(
                    (args.Value<string>("projectFile") ?? string.Empty).Trim());
                var reason = (args.Value<string>("reason") ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(reason))
                    return ToolHandlerUtilities.Fail("reason is required for external project execution.");

                FlowBloxProject project;
                var isProjectSpace = !string.IsNullOrWhiteSpace(args.Value<string>("projectSpaceGuid"));
                if (isProjectSpace)
                    project = await ProjectSpaceToolClient.LoadAsync(args, ct).ConfigureAwait(false);
                else
                {
                    if (string.IsNullOrWhiteSpace(projectFile))
                        return ToolHandlerUtilities.Fail("projectFile or projectSpaceGuid is required.");
                    if (!File.Exists(projectFile))
                        return ToolHandlerUtilities.Fail($"Project file does not exist: {projectFile}");
                    project = SkillProjectSerializer.FromFile(projectFile);
                }

                var outputFields = ReadOutputFieldFilter(args["outputFields"]);
                ValidateOutputFields(project, outputFields);
                var request = new RunnerRequest
                {
                    ProjectFile = isProjectSpace ? null : Path.GetFullPath(projectFile),
                    ProjectSpaceGuid = isProjectSpace ? project.ProjectSpaceGuid : null,
                    ProjectSpaceVersion = isProjectSpace ? project.ProjectSpaceVersion : null,
                    AutoRestart = false,
                    AbortOnError = true,
                    AbortOnWarning = false,
                    EnableAppNotifications = false,
                    UserFields = ProjectExecutionToolUtilities.ResolveInputParameters(
                        args["inputParameters"] as JObject,
                        project)
                };

                var response = Execute(request, ct);
                var normalizedOutputs = NormalizeOutputs(response);
                var runId = Guid.NewGuid().ToString("N");
                AiAssistantProjectRunState.Set(
                    ToolHandlerUtilities.CurrentSessionGuid,
                    new AiAssistantProjectRunSnapshot
                    {
                        RunId = runId,
                        ProjectName = response.ProjectName ?? project.ProjectName ?? string.Empty,
                        Outputs = normalizedOutputs
                    });

                var payload = BuildResponse(response, normalizedOutputs, outputFields, runId);
                return response.Success
                    ? ToolHandlerUtilities.Ok(payload)
                    : ToolHandlerUtilities.Fail("Project execution failed.", payload);
            }
            catch (OperationCanceledException)
            {
                return ToolHandlerUtilities.Fail("Project execution was cancelled.");
            }
            catch (Exception ex)
            {
                return ToolHandlerUtilities.Fail(ex.Message);
            }
        }

        private static RunnerResponse Execute(RunnerRequest request, CancellationToken ct)
        {
            var workDirectory = Path.Combine(Path.GetTempPath(), "FlowBloxAiAssistantExecute", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);
            var requestFile = Path.Combine(workDirectory, "request.json");
            var responseFile = Path.Combine(workDirectory, "response.json");
            ProjectExecutionToolUtilities.WriteJson(requestFile, request);

            var command = RunnerHostResolver.Resolve();
            ProjectExecutionToolUtilities.StartRunnerHost(command, requestFile, responseFile, ct);

            if (!File.Exists(responseFile))
                throw new InvalidOperationException($"RunnerHost did not create a response file: {responseFile}");

            return JsonSerializer.Deserialize<RunnerResponse>(
                       File.ReadAllText(responseFile),
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new InvalidOperationException("Runner response could not be deserialized.");
        }

        private static HashSet<string> ReadOutputFieldFilter(JToken? token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (token is not JArray array)
                throw new InvalidOperationException("outputFields must be an array of property names.");

            return array.Values<string>()
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static void ValidateOutputFields(FlowBloxProject project, IReadOnlySet<string> requestedFields)
        {
            if (requestedFields.Count == 0)
                return;

            var available = project.FlowBlocks
                .OfType<ProjectOutputFlowBlock>()
                .SelectMany(x => x.MappingEntries)
                .Select(x => x.OutputPropertyName)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = requestedFields.Where(x => !available.Contains(x)).ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Unknown output field(s): {string.Join(", ", missing)}. Available output fields: {string.Join(", ", available.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))}.");
            }
        }

        private static JObject NormalizeOutputs(RunnerResponse response)
        {
            var outputs = new JObject();
            foreach (var output in response.Outputs ?? new Dictionary<string, List<ProjectOutputDatasetDto>>())
            {
                var datasets = new JArray();
                foreach (var dataset in output.Value ?? [])
                {
                    var values = new JObject();
                    foreach (var value in dataset.Values ?? new Dictionary<string, object>())
                        values[value.Key] = NormalizeValue(value.Value);

                    datasets.Add(new JObject
                    {
                        ["createdUtc"] = dataset.CreatedUtc,
                        ["values"] = values
                    });
                }

                outputs[output.Key] = datasets;
            }

            return outputs;
        }

        private static JToken NormalizeValue(object? value)
        {
            if (value == null)
                return JValue.CreateNull();
            if (value is JsonElement jsonElement)
                return jsonElement.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? JValue.CreateNull()
                    : JToken.Parse(jsonElement.GetRawText());
            return JToken.FromObject(value);
        }

        private static JObject BuildResponse(
            RunnerResponse response,
            JObject normalizedOutputs,
            IReadOnlySet<string> outputFields,
            string runId)
        {
            var limiter = FieldValueResponseLimiter.FromConfiguration();
            var outputResult = new JObject();
            var omittedDatasets = 0;
            foreach (var output in normalizedOutputs.Properties())
            {
                var datasets = output.Value as JArray ?? new JArray();
                if (outputFields.Count > 0 && !datasets
                        .OfType<JObject>()
                        .Select(x => x["values"] as JObject)
                        .Where(x => x != null)
                        .SelectMany(x => x!.Properties())
                        .Any(x => outputFields.Contains(x.Name)))
                {
                    continue;
                }

                var limitedDatasets = new JArray();
                for (var index = 0; index < datasets.Count; index++)
                {
                    if (limiter.RemainingTokens <= 0)
                    {
                        omittedDatasets += datasets.Count - index;
                        break;
                    }

                    var dataset = (JObject)datasets[index]!;
                    var values = (JObject)dataset["values"]!;
                    var limitedValues = new JObject();
                    var valueInfo = new JObject();
                    foreach (var value in values.Properties().Where(x => outputFields.Count == 0 || outputFields.Contains(x.Name)))
                    {
                        var text = value.Value.Type == JTokenType.String
                            ? value.Value.Value<string>() ?? string.Empty
                            : value.Value.ToString(Newtonsoft.Json.Formatting.None);
                        var limited = limiter.Limit(text);
                        limitedValues[value.Name] = limited.IsComplete ? value.Value.DeepClone() : limited.Value;
                        if (!limited.IsComplete)
                            valueInfo[value.Name] = limited.ToMetadata();
                    }

                    var limitedDataset = new JObject
                    {
                        ["datasetIndex"] = index,
                        ["createdUtc"] = dataset["createdUtc"]?.DeepClone(),
                        ["values"] = limitedValues
                    };
                    if (valueInfo.HasValues)
                        limitedDataset["valueInfo"] = valueInfo;
                    limitedDatasets.Add(limitedDataset);
                }

                outputResult[output.Name] = limitedDatasets;
            }

            var payload = new JObject
            {
                ["runId"] = runId,
                ["projectName"] = response.ProjectName ?? string.Empty,
                ["success"] = response.Success,
                ["exitCode"] = response.ExitCode,
                ["errorMessage"] = response.ErrorMessage ?? string.Empty,
                ["warnings"] = new JArray(response.Warnings ?? []),
                ["outputs"] = outputResult,
                ["fieldValueOutput"] = limiter.CreateMetadata()
            };
            if (!string.IsNullOrWhiteSpace(response.AbortMessage))
                payload["abortMessage"] = response.AbortMessage;
            if (!string.IsNullOrWhiteSpace(response.AbortException))
                payload["abortException"] = response.AbortException;
            if (omittedDatasets > 0)
                payload["omittedDatasetCount"] = omittedDatasets;
            return payload;
        }
    }
}
