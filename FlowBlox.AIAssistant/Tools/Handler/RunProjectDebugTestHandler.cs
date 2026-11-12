using System.Diagnostics;
using System.Text.Json;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Handler
{
    internal sealed class RunProjectDebugTestHandler : ToolHandlerBase
    {
        public override string Name => "RunProjectDebugTest";

        public override ToolDefinition Definition => ToolHandlerUtilities.CreateDefinition(
            Name,
            "Runs the current project or an explicitly supplied project file in RunnerHost debug mode and returns runtime protocol output. " +
            "For an explicitly supplied external project or an active skill project, provide a reason and call directly; FlowBlox requests user approval unless the project is already in the user's AI.ApprovedSkillProjects option. The main project requires neither. " +
            "FieldValueChanges and GeneratedResults must be read via GetLastDebugArtefact. " +
            "warningCount/errorCount count runtime reports; warningNotificationCount/errorNotificationCount count flow-block notifications separately. " +
            "If a target is set, runtime always stops at target: default is stopping before target execution. Set includeTargetExecution=true to execute target once and then stop.",
            new JObject
            {
                ["targetFlowBlockName"] = "string?",
                ["maxRuntimeSeconds"] = "int? (default: 30)",
                ["includeTargetExecution"] = "bool? (default: false, only relevant when target is set)",
                ["maxCapturedFieldValueChanges"] = "int? (default: 100)",
                ["maxProtocolEntries"] = "optional: int (default: 250). Returns the latest entries in chronological order.",
                ["projectFile"] = "string? (defaults to the project currently targeted by project tools)",
                ["reason"] = "string? (required for an external or active skill project; explain why execution is needed)",
                ["inputParameters"] = "object? (input user-field name to textual value; values support FlowBlox placeholders)",
                ["usageHint"] = "Use this call for protocol output only. Read FieldValueChanges/GeneratedResults via GetLastDebugArtefact. Prefer one optimistic debug run first (usually includeTargetExecution=false). Tune maxProtocolEntries as needed."
            });

        public override Task<ToolResponse> HandleAsync(JObject args, CancellationToken ct)
        {
            try
            {
                var currentProject = ToolHandlerUtilities.GetProject();
                var requestedProjectFile = FlowBloxFieldHelper.ReplaceFieldsInString(
                    (args.Value<string>("projectFile") ?? string.Empty).Trim());
                var usesExternalProjectFile = !string.IsNullOrWhiteSpace(requestedProjectFile);
                var executesSkill = ToolHandlerUtilities.IsSkillProjectActive();
                var reason = (args.Value<string>("reason") ?? string.Empty).Trim();
                if ((usesExternalProjectFile || executesSkill) && string.IsNullOrWhiteSpace(reason))
                    return Task.FromResult(ToolHandlerUtilities.Fail("reason is required when an external or skill project is executed."));
                if (usesExternalProjectFile && !File.Exists(requestedProjectFile))
                    return Task.FromResult(ToolHandlerUtilities.Fail($"Project file does not exist: {requestedProjectFile}"));

                FlowBloxProject executionProject;
                if (usesExternalProjectFile)
                {
                    using (new DisableExtensionLoading())
                    {
                        executionProject = FlowBloxProject.FromFile(requestedProjectFile);
                    }
                }
                else
                {
                    currentProject.RefreshOrderedTopLevelCollectionsForSerialization();
                    executionProject = currentProject;
                }

                var inputParameters = ProjectExecutionToolUtilities.ResolveInputParameters(
                    args["inputParameters"] as JObject,
                    executionProject);

                var maxRuntimeSeconds = Math.Max(1, args.Value<int?>("maxRuntimeSeconds") ?? 30);
                var includeTargetExecution = args.Value<bool?>("includeTargetExecution") ?? false;
                var targetFlowBlockName = (args.Value<string>("targetFlowBlockName") ?? string.Empty).Trim();
                var maxCapturedChanges = Math.Max(0, args.Value<int?>("maxCapturedFieldValueChanges") ?? 100);
                var maxProtocolEntries = Math.Max(
                    1,
                    args.Value<int?>("maxProtocolEntries")
                    ?? args.Value<int?>("maxProtocolPreviewEntries")
                    ?? 250);

                if (!string.IsNullOrWhiteSpace(targetFlowBlockName))
                {
                    var exists = executionProject.FlowBlocks.Any(x =>
                        string.Equals(x.Name, targetFlowBlockName, StringComparison.OrdinalIgnoreCase));
                    if (!exists)
                        return Task.FromResult(ToolHandlerUtilities.Fail($"Target flow block '{targetFlowBlockName}' does not exist in current project."));
                }

                var workDir = Path.Combine(Path.GetTempPath(), "FlowBloxAiAssistantDebug", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workDir);

                var projectFile = usesExternalProjectFile
                    ? Path.GetFullPath(requestedProjectFile)
                    : Path.Combine(workDir, "project.fbprj");
                var requestFile = Path.Combine(workDir, "request.json");
                var responseFile = Path.Combine(workDir, "response.json");
                var debuggingResultFile = Path.Combine(workDir, "debugging-result.json");

                if (!usesExternalProjectFile)
                    executionProject.Save(projectFile);

                var runnerRequest = new RunnerRequest
                {
                    ProjectFile = projectFile,
                    AutoRestart = false,
                    AbortOnError = false,
                    AbortOnWarning = false,
                    UserFields = inputParameters,
                    ExternalDebuggingInformation = new RunnerExternalDebuggingInformation
                    {
                        MaxRuntimeSeconds = maxRuntimeSeconds,
                        TargetFlowBlockName = string.IsNullOrWhiteSpace(targetFlowBlockName) ? null : targetFlowBlockName,
                        IncludeTargetExecution = includeTargetExecution,
                        MaxCapturedFieldValueChanges = maxCapturedChanges,
                        DebuggingResultFilePath = debuggingResultFile
                    }
                };

                ProjectExecutionToolUtilities.WriteJson(requestFile, runnerRequest);

                var cmd = RunnerHostResolver.Resolve();
                var exitCode = ProjectExecutionToolUtilities.StartRunnerHost(cmd, requestFile, responseFile, ct);

                if (!File.Exists(responseFile))
                    return Task.FromResult(ToolHandlerUtilities.Fail($"RunnerHost did not create response file: {responseFile}"));

                var responseJson = File.ReadAllText(responseFile);
                var response = JsonSerializer.Deserialize<RunnerResponse>(
                    responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (response == null)
                    return Task.FromResult(ToolHandlerUtilities.Fail("RunnerResponse could not be deserialized."));

                JObject debuggingResult;
                if (File.Exists(debuggingResultFile))
                {
                    var debuggingJson = File.ReadAllText(debuggingResultFile);
                    debuggingResult = string.IsNullOrWhiteSpace(debuggingJson)
                        ? new JObject()
                        : JObject.Parse(debuggingJson);
                }
                else
                {
                    debuggingResult = new JObject();
                }

                var runId = Guid.NewGuid().ToString("N");
                AiAssistantDebugRunState.Set(new AiAssistantDebugRunSnapshot
                {
                    RunId = runId,
                    CreatedUtc = DateTime.UtcNow,
                    DebuggingResultFilePath = debuggingResultFile,
                    DebuggingResult = debuggingResult
                });

                var protocol = GetArrayIgnoreCase(debuggingResult, "Protocol", "protocol");
                var warnings = GetArrayIgnoreCase(debuggingResult, "Warnings", "warnings");
                var errors = GetArrayIgnoreCase(debuggingResult, "Errors", "errors");
                var warningNotifications = GetArrayIgnoreCase(debuggingResult, "WarningNotifications", "warningNotifications");
                var errorNotifications = GetArrayIgnoreCase(debuggingResult, "ErrorNotifications", "errorNotifications");
                var protocolLimited = BuildProtocol(protocol, maxProtocolEntries);

                var payload = new JObject
                {
                    ["runId"] = runId,
                    ["runnerExitCode"] = exitCode,
                    ["success"] = response.Success,
                    ["responseExitCode"] = response.ExitCode,
                    ["errorMessage"] = response.ErrorMessage ?? string.Empty,
                    ["exception"] = response.Exception ?? string.Empty,
                    ["cancellationKind"] = response.CancellationKind?.ToString() ?? string.Empty,
                    ["cancellationReason"] = response.CancellationReason ?? string.Empty,
                    ["includeTargetExecution"] = includeTargetExecution,
                    ["projectFile"] = projectFile,
                    ["externalProjectFile"] = usesExternalProjectFile,
                    ["skill_project"] = executesSkill,
                    ["inputParameterCount"] = inputParameters.Count,
                    ["debuggingResultFilePath"] = response.DebuggingResultFilePath ?? debuggingResultFile,
                    ["protocolEntryCount"] = protocolLimited.Count,
                    ["protocolEntryCountTotal"] = protocol.Count,
                    ["maxProtocolEntries"] = maxProtocolEntries,
                    ["warningCount"] = warnings.Count,
                    ["errorCount"] = errors.Count,
                    ["warningNotificationCount"] = warningNotifications.Count,
                    ["errorNotificationCount"] = errorNotifications.Count,
                    ["protocol"] = protocolLimited,
                    ["outputSummary"] = BuildOutputSummary(response)
                };

                if (response.Success || response.CancellationKind == RuntimeCancellationKind.DebuggingTargetReached)
                    return Task.FromResult(ToolHandlerUtilities.Ok(payload));

                return Task.FromResult(ToolHandlerUtilities.Fail("Debug test run failed.", payload));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ToolHandlerUtilities.Fail(ex.Message));
            }
        }

        private static JArray BuildProtocol(JArray protocol, int maxEntries)
        {
            return ProtocolEntryWindowing.TakeLastInOriginalOrder(protocol, maxEntries);
        }

        private static JArray GetArrayIgnoreCase(JObject root, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (root[key] is JArray direct)
                    return direct;
            }

            var property = root.Properties()
                .FirstOrDefault(x => keys.Any(k => string.Equals(k, x.Name, StringComparison.OrdinalIgnoreCase)));

            return property?.Value as JArray ?? new JArray();
        }

        private static JObject BuildOutputSummary(RunnerResponse response)
        {
            var outputs = new JObject();
            foreach (var kvp in response.Outputs)
            {
                outputs[kvp.Key] = kvp.Value?.Count ?? 0;
            }

            return outputs;
        }

    }
}
