using FlowBlox.Core.Authentication;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Runner
{
    public static class FlowBloxProjectRunner
    {
        public static event Action<RunnerLogMessage> LogMessageCreated;

        private static void Report(string message, FlowBloxLogLevel level = FlowBloxLogLevel.Info)
        {
            LogMessageCreated?.Invoke(new RunnerLogMessage
            {
                UtcTimestamp = DateTime.UtcNow,
                LogLevel = level,
                Message = message
            });
        }

        private static async Task<FlowBloxProject> OpenProjectFromProjectSpaceAsync(
            string projectGuid,
            int? projectVersion,
            CancellationToken cancellationToken = default)
        {
            // Auto login
            var flowBloxAutoLoginExecutor = new FlowBloxAutoLoginExecutor();
            await flowBloxAutoLoginExecutor.TryAutoLoginAsync().ConfigureAwait(false);

            // Build Web API client and token
            var baseUrl = FlowBloxOptions.GetOptionInstance()
                .OptionCollection[OptionsConstants.ApiProjectServiceBaseUrl]
                .Value;

            var webApi = new FlowBloxWebApiService(baseUrl);
            var token = FlowBloxAccountManager.Instance.GetUserToken(baseUrl);

            // Load the project from ProjectSpace
            return await FlowBloxProject.FromProjectSpaceGuidAsync(
                    projectGuid,
                    projectVersion,
                    token,
                    webApi)
                .ConfigureAwait(false);
        }

        private static FlowBloxProject OpenProjectFromProjectSpace(string projectGuid, int? projectVersion)
        {
            return OpenProjectFromProjectSpaceAsync(projectGuid, projectVersion)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>
        /// Runs a FlowBlox project in-process. Use RunnerHost for out-of-process sandbox execution.
        /// </summary>
        public static RunnerResponse Run(RunnerRequest request)
        {
            var response = new RunnerResponse
            {
                StartedUtc = DateTime.UtcNow
            };

            FlowBloxRuntime runtime = null;
            CancellationTokenSource timeoutCts = null;

            try
            {
                if (request == null)
                    throw new ArgumentNullException(nameof(request));

                var hasProjectSpace = !string.IsNullOrWhiteSpace(request.ProjectSpaceGuid);
                var hasProjectFile = !string.IsNullOrWhiteSpace(request.ProjectFile);

                if (!hasProjectSpace && !hasProjectFile)
                    throw new ArgumentException("Either ProjectFile or ProjectSpaceGuid must be provided.", nameof(request));

                FlowBloxProject project;

                if (hasProjectSpace)
                {
                    if (request.ProjectSpaceVersion.HasValue)
                        Report($"Loading project from ProjectSpaceGuid '{request.ProjectSpaceGuid}' and Version {request.ProjectSpaceVersion}...");
                    else
                        Report($"Loading project from ProjectSpaceGuid '{request.ProjectSpaceGuid}'...");

                    project = OpenProjectFromProjectSpace(request.ProjectSpaceGuid, request.ProjectSpaceVersion);
                }
                else
                {
                    Report($"Loading project from '{request.ProjectFile}'...");
                    project = FlowBloxProject.FromFile(request.ProjectFile);
                }

                if (project == null)
                    throw new InvalidOperationException("Project could not be loaded.");

                FlowBloxProjectManager.Instance.ActiveProject = project;
                response.ProjectName = project.ProjectName;

                // Ensure options are initialized, then apply overrides.
                var flowBloxOptions = FlowBloxOptions.GetOptionInstance();
                flowBloxOptions.InitDefaults(false);
                ApplyOptionOverrides(flowBloxOptions, request.OptionOverrides);

                // Apply user field overrides (inputs).
                ApplyUserFieldOverrides(request.UserFields);

                // Create runtime.
                runtime = new FlowBloxRuntime(project)
                {
                    IsNoDesignerMode = true,
                    AutoRestart = request.AutoRestart, // Note: ExecuteProjectFlowBlock/RunnerHost may force this to false before calling the runner.
                    ExternalDebuggingInformation = MapDebuggingInformation(request.ExternalDebuggingInformation)
                };

                response.LogfilePath = runtime.GetLogfilePath();
                Report($"Logfile path: {response.LogfilePath}");

                // Forward runtime logs and optionally abort on warnings/errors.
                string abortReason = null;

                runtime.LogEntryCreated += (rt, msg, level, exception) =>
                {
                    if (level == FlowBloxLogLevel.Warning)
                    {
                        lock (response.Warnings)
                            response.Warnings.Add(msg);
                    }

                    if (abortReason == null)
                    {
                        if (request.AbortOnError && level == FlowBloxLogLevel.Error)
                            abortReason = "Aborted due to an error log message.";

                        if (request.AbortOnWarning && level == FlowBloxLogLevel.Warning)
                            abortReason = "Aborted due to a warning log message.";

                        if (abortReason != null)
                        {
                            response.AbortMessage = msg;
                            response.AbortException = exception?.ToString();
                        }
                    }

                    if (request.AbortOnError && level == FlowBloxLogLevel.Error)
                        rt.CancelExecution(RuntimeCancellationKind.AbortOnWarningOrError, abortReason);

                    if (request.AbortOnWarning && level == FlowBloxLogLevel.Warning)
                        rt.CancelExecution(RuntimeCancellationKind.AbortOnWarningOrError, abortReason);
                };

                runtime.LogMessageCreated += (_, msg, level) => Report(msg, level);

                StartDebugTimeoutIfConfigured(runtime, out timeoutCts);

                Report($"Execution of project '{project.ProjectName}' started...");
                runtime.Execute();

                timeoutCts?.Cancel();

                CollectOutputs(runtime, response);
                TryWriteDebuggingResult(runtime, request.ExternalDebuggingInformation, response);

                response.CancellationKind = runtime.CancellationContext?.CancellationKind;
                response.CancellationReason = runtime.CancellationContext?.Reason;

                if (runtime.Aborted)
                {
                    if (runtime.CancellationContext?.CancellationKind == RuntimeCancellationKind.DebuggingTargetReached)
                    {
                        response.Success = true;
                        response.ExitCode = RunnerExitCodes.Success;
                        response.Warnings.Add(runtime.CancellationContext.Reason);
                        Report(runtime.CancellationContext.Reason, FlowBloxLogLevel.Info);
                    }
                    else
                    {
                        response.Success = false;
                        response.ExitCode = RunnerExitCodes.Aborted;
                        response.ErrorMessage = abortReason ?? runtime.CancellationContext?.Reason ?? "Execution aborted.";
                        Report(response.ErrorMessage, FlowBloxLogLevel.Warning);
                    }

                    return response;
                }

                response.Success = true;
                response.ExitCode = RunnerExitCodes.Success;

                Report($"Execution of project '{project.ProjectName}' finished.", FlowBloxLogLevel.Info);
                return response;
            }
            catch (InvalidOperationException ex)
            {
                response.Success = false;
                response.ExitCode = RunnerExitCodes.ValidationError;
                response.ErrorMessage = ex.Message;
                response.Exception = ex.ToString();

                Report(ex.Message, FlowBloxLogLevel.Error);
                return response;
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.ExitCode = RunnerExitCodes.RuntimeError;
                response.ErrorMessage = ex.Message;
                response.Exception = ex.ToString();

                Report(ex.Message, FlowBloxLogLevel.Error);
                return response;
            }
            finally
            {
                timeoutCts?.Cancel();
                timeoutCts?.Dispose();
                response.FinishedUtc = DateTime.UtcNow;
            }
        }

        private static void StartDebugTimeoutIfConfigured(
            FlowBloxRuntime runtime,
            out CancellationTokenSource timeoutCts)
        {
            timeoutCts = null;

            if (runtime?.ExternalDebuggingInformation == null)
                return;

            var maxRuntimeSeconds = Math.Max(1, runtime.ExternalDebuggingInformation.MaxRuntimeSeconds);
            var cts = new CancellationTokenSource();
            timeoutCts = cts;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(maxRuntimeSeconds), cts.Token).ConfigureAwait(false);

                    if (!cts.IsCancellationRequested && runtime.Running && !runtime.Aborted)
                    {
                        runtime.CancelExecution(
                            RuntimeCancellationKind.DebuggingTimeout,
                            $"Debug runtime timeout reached ({maxRuntimeSeconds}s).");
                    }
                }
                catch (TaskCanceledException ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        "The debug runtime timeout monitor was cancelled before completion.",
                        ex);
                }
            });
        }

        private static RuntimeExternalDebuggingInformation MapDebuggingInformation(RunnerExternalDebuggingInformation debugging)
        {
            if (debugging == null)
                return null;

            return new RuntimeExternalDebuggingInformation
            {
                MaxRuntimeSeconds = Math.Max(1, debugging.MaxRuntimeSeconds),
                TargetFlowBlockName = debugging.TargetFlowBlockName,
                IncludeTargetExecution = debugging.IncludeTargetExecution,
                MaxCapturedFieldValueChanges = Math.Max(0, debugging.MaxCapturedFieldValueChanges)
            };
        }

        private static void CollectOutputs(FlowBloxRuntime runtime, RunnerResponse response)
        {
            var outputs = runtime.GetAllOutputs();
            foreach (var outputKvp in outputs)
            {
                var list = new List<ProjectOutputDatasetDto>();

                foreach (var ds in outputKvp.Value)
                {
                    list.Add(new ProjectOutputDatasetDto
                    {
                        OutputName = ds.OutputName,
                        CreatedUtc = ds.CreatedUtc,
                        Values = ds.Values ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
                    });
                }

                response.Outputs[outputKvp.Key] = list;
            }
        }

        private static void TryWriteDebuggingResult(
            FlowBloxRuntime runtime,
            RunnerExternalDebuggingInformation requestDebugging,
            RunnerResponse response)
        {
            if (requestDebugging == null)
                return;

            var debuggingResult = runtime.GetDebuggingResult();
            if (debuggingResult == null)
                return;

            var filePath = requestDebugging.DebuggingResultFilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                var fileName = $"debugging_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
                filePath = Path.Combine(Path.GetTempPath(), "FlowBloxRunner", fileName);
            }

            RunnerJson.WriteFile(filePath, debuggingResult);
            response.DebuggingResultFilePath = filePath;
        }

        private static void ApplyOptionOverrides(FlowBloxOptions flowBloxOptions, Dictionary<string, string> overrides)
        {
            if (overrides == null || overrides.Count == 0)
                return;

            foreach (var kvp in overrides)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                var opt = flowBloxOptions.GetOption(key);
                if (opt == null)
                {
                    Report($"Option override ignored: '{key}' not found.", FlowBloxLogLevel.Warning);
                    continue;
                }

                opt.Value = value;
                Report($"Option '{key}' overridden with '{value}'.", FlowBloxLogLevel.Info);
            }
        }

        private static void ApplyUserFieldOverrides(Dictionary<string, string> userFields)
        {
            userFields ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var fieldElement in FlowBloxRegistryProvider.GetRegistry().GetUserFields(UserFieldTypes.Input))
            {
                if (userFields.TryGetValue(fieldElement.Name, out var stringValue))
                {
                    fieldElement.StringValue = stringValue;
                    var displayValue = fieldElement.IsPassword
                        ? GlobalConstants.HiddenSensitiveValue
                        : stringValue;

                    Report($"User field '{fieldElement.Name}' set to '{displayValue}'.", FlowBloxLogLevel.Info);
                }
                else
                {
                    if (string.IsNullOrEmpty(fieldElement.StringValue))
                        Report($"No value provided for input field '{fieldElement.Name}'.", FlowBloxLogLevel.Warning);
                    else
                    {
                        var displayValue = fieldElement.IsPassword
                            ? GlobalConstants.HiddenSensitiveValue
                            : fieldElement.StringValue;
                        Report($"No value provided for input field '{fieldElement.Name}', using current value '{displayValue}'.", FlowBloxLogLevel.Info);
                    }
                }
            }
        }
    }
}
