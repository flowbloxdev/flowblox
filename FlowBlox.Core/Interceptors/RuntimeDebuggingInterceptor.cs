using System.Diagnostics;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Util;

namespace FlowBlox.Core.Interceptors
{
    public sealed class RuntimeDebuggingInterceptor : RuntimeInterceptorBase
    {
        private readonly object _sync = new object();
        private Stopwatch _stopwatch;
        private RuntimeDebuggingResult _result;
        private int _nextFieldChangeId;
        private int _nextGeneratedResultId;

        private bool IsEnabled => Runtime?.ExternalDebuggingInformation != null;

        public override void NotifyBeforeRuntimeStarted()
        {
            if (!IsEnabled)
                return;

            var config = Runtime.ExternalDebuggingInformation;
            _stopwatch = Stopwatch.StartNew();
            _nextFieldChangeId = 0;
            _nextGeneratedResultId = 0;

            _result = new RuntimeDebuggingResult
            {
                ProjectName = Runtime.Project?.ProjectName ?? string.Empty,
                StartedUtc = DateTime.UtcNow,
                TargetFlowBlockName = config.TargetFlowBlockName,
                MaxRuntimeSeconds = Math.Max(1, config.MaxRuntimeSeconds),
                IncludeTargetExecution = config.IncludeTargetExecution,
                MaxCapturedFieldValueChanges = Math.Max(0, config.MaxCapturedFieldValueChanges)
            };

            AppendProtocol("RuntimeStarted", "Runtime started.");
            Runtime.LogEntryCreated -= OnRuntimeLogEntryCreated;
            Runtime.LogEntryCreated += OnRuntimeLogEntryCreated;
        }

        public override void NotifyRuntimeFinished()
        {
            if (!IsEnabled)
                return;

            Runtime.LogEntryCreated -= OnRuntimeLogEntryCreated;

            lock (_sync)
            {
                AppendProtocol("RuntimeFinished", "Runtime finished.");
                _result.Aborted = Runtime.Aborted;
                _result.Cancellation = Runtime.CancellationContext;
                _result.FinishedUtc = DateTime.UtcNow;
                Runtime.SetDebuggingResult(_result);
            }
        }

        public override void NotifyRuntimeCancelled(RuntimeCancellationContext cancellationContext)
        {
            if (!IsEnabled)
                return;

            var reason = cancellationContext?.Reason ?? "Cancellation requested.";
            if (cancellationContext?.CancellationKind == RuntimeCancellationKind.DebuggingTargetReached)
            {
                AppendProtocol("DebugTargetReached", reason);
                return;
            }

            AppendProtocol("RuntimeCancelled", reason);
        }

        public override void NotifyRuntimeAborted(Exception exception)
        {
            if (!IsEnabled)
                return;

            AppendProtocol(
                "RuntimeAborted",
                "Runtime aborted due to an unexpected exception.",
                details: exception?.ToString());
        }

        public override void NotifyInvocationStarted(BaseFlowBlock flowBlock)
        {
            if (!IsEnabled)
                return;

            AppendProtocol(
                "InvocationStarted",
                $"Invocation started for flow block '{flowBlock?.Name}'.",
                flowBlock?.Name);
        }

        public override void NotifyBeforeFlowBlockValidation(BaseFlowBlock flowBlock)
        {
            if (!IsEnabled || flowBlock == null || Runtime.Aborted)
                return;

            var config = Runtime.ExternalDebuggingInformation;
            if (config.IncludeTargetExecution)
                return;

            if (!IsTarget(flowBlock.Name, config.TargetFlowBlockName))
                return;

            Runtime.CancelExecution(
                RuntimeCancellationKind.DebuggingTargetReached,
                $"Debug target flow block reached before execution: '{flowBlock.Name}'.");
        }

        public override bool ShouldCancelValidation(BaseFlowBlock flowBlock, bool validationFinished)
        {
            if (!IsEnabled || flowBlock == null)
                return false;

            var config = Runtime.ExternalDebuggingInformation;
            if (!IsTarget(flowBlock.Name, config.TargetFlowBlockName))
                return false;

            if (!validationFinished)
                return !config.IncludeTargetExecution;

            return config.IncludeTargetExecution;
        }

        public override void NotifyInvocationFinished(BaseFlowBlock flowBlock)
        {
            if (!IsEnabled)
                return;

            var flowBlockName = flowBlock?.Name;
            AppendProtocol(
                "InvocationFinished",
                $"Invocation finished for flow block '{flowBlockName}'.",
                flowBlockName);

            var config = Runtime.ExternalDebuggingInformation;
            if (config.IncludeTargetExecution
                && !string.IsNullOrWhiteSpace(config.TargetFlowBlockName)
                && string.Equals(config.TargetFlowBlockName, flowBlockName, StringComparison.OrdinalIgnoreCase)
                && !Runtime.Aborted)
            {
                Runtime.CancelExecution(
                    RuntimeCancellationKind.DebuggingTargetReached,
                    $"Debug target flow block reached after execution: '{flowBlockName}'.");
            }
        }

        public override void NotifyPreconditionsNotMet(BaseFlowBlock flowBlock, IReadOnlyList<string> messages)
        {
            if (!IsEnabled)
                return;

            var flowBlockName = flowBlock?.Name ?? string.Empty;
            var list = (messages ?? Array.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            lock (_sync)
            {
                if (_result == null)
                    return;

                _result.PreconditionFailures.Add(new RuntimeDebugPreconditionFailureEntry
                {
                    Elapsed = GetElapsed(),
                    FlowBlockName = flowBlockName,
                    Messages = list
                });
            }

            var summary = list.Count == 0
                ? "Preconditions were not met."
                : $"Preconditions were not met: {string.Join(" | ", list)}";
            AppendProtocol("PreconditionsNotMet", summary, flowBlockName);
        }

        public override void NotifyIterationStarted(BaseFlowBlock flowBlock)
        {
            if (!IsEnabled)
                return;

            AppendProtocol(
                "IterationStarted",
                $"Iteration started at flow block '{flowBlock?.Name}'.",
                flowBlock?.Name);
        }

        public override void NotifyIterationFinished(BaseFlowBlock flowBlock)
        {
            if (!IsEnabled)
                return;

            AppendProtocol(
                "IterationFinished",
                $"Iteration finished at flow block '{flowBlock?.Name}'.",
                flowBlock?.Name);
        }

        public override void NotifyResultDatasetsGenerated(RuntimeResultDatasetSummary resultDatasetSummary)
        {
            if (!IsEnabled || resultDatasetSummary == null)
                return;

            var flowBlockName = resultDatasetSummary.FlowBlock?.Name;
            var generatedResultId = Interlocked.Increment(ref _nextGeneratedResultId);
            var serializedDatasets = RuntimeGeneratedResultMapper.MapDatasets(resultDatasetSummary.Datasets);

            lock (_sync)
            {
                if (_result == null)
                    return;

                _result.TotalGeneratedResults++;
                _result.GeneratedResults.Add(new RuntimeGeneratedResultDetails
                {
                    Id = generatedResultId,
                    Elapsed = GetElapsed(),
                    FlowBlockName = flowBlockName ?? string.Empty,
                    DatasetCount = resultDatasetSummary.DatasetCount,
                    Datasets = serializedDatasets
                });
            }

            AppendProtocol(
                "ResultDatasetsGenerated",
                $"Generated result with {resultDatasetSummary.DatasetCount} generated dataset(s) in total, see id: {generatedResultId}.",
                flowBlockName,
                null,
                generatedResultId);
        }

        public override void NotifyBeforeInputProcessing(RuntimeInputProcessingSummary inputProcessingSummary)
        {
            if (!IsEnabled || inputProcessingSummary == null)
                return;

            var flowBlockName = inputProcessingSummary.FlowBlock?.Name;
            var iterationContextName = inputProcessingSummary.IterationContext?.Name ?? string.Empty;

            var orderedBehaviours = inputProcessingSummary.InputBehaviours
                .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value != null)
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"- {x.Key}: {x.Value}")
                .ToList();

            var details = orderedBehaviours.Count == 0
                ? $"IterationContext: {iterationContextName}"
                : $"IterationContext: {iterationContextName}{Environment.NewLine}{string.Join(Environment.NewLine, orderedBehaviours)}";

            AppendProtocol(
                "BeforeInputProcessing",
                $"Iteration for '{iterationContextName}' completed. Generated {inputProcessingSummary.InputDatasetCount} input dataset(s) for '{flowBlockName}'.",
                flowBlockName,
                details: details);
        }

        public override void NotifyWarning(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            CaptureProblem(FlowBloxLogLevel.Warning, message, exception, baseFlowBlock, fromReport: false);
        }

        public override void NotifyError(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            CaptureProblem(FlowBloxLogLevel.Error, message, exception, baseFlowBlock, fromReport: false);
        }

        private void OnRuntimeLogEntryCreated(BaseRuntime runtime, string message, FlowBloxLogLevel level, Exception exception)
        {
            if (level == FlowBloxLogLevel.Warning || level == FlowBloxLogLevel.Error)
                CaptureProblem(level, message, exception, null, fromReport: true);
        }

        private void CaptureProblem(
            FlowBloxLogLevel level,
            string message,
            Exception exception,
            BaseFlowBlock flowBlock,
            bool fromReport)
        {
            if (!IsEnabled)
                return;

            var text = message ?? string.Empty;
            var details = exception?.ToString() ?? string.Empty;

            lock (_sync)
            {
                if (_result == null)
                    return;

                var problem = new RuntimeDebugProblemEntry
                {
                    Elapsed = GetElapsed(),
                    FlowBlockName = flowBlock?.Name ?? string.Empty,
                    Message = text,
                    Exception = details
                };
                List<RuntimeDebugProblemEntry> problems;
                if (fromReport)
                    problems = level == FlowBloxLogLevel.Warning ? _result.Warnings : _result.Errors;
                else
                    problems = level == FlowBloxLogLevel.Warning ? _result.WarningNotifications : _result.ErrorNotifications;

                problems.Add(problem);
                var eventType = fromReport ? level.ToString() : level + "Notification";
                AppendProtocol(eventType, text, flowBlock?.Name, details: exception?.ToString());
            }
        }

        public override void NotifyFieldChange(FieldElement fieldElement, string oldValue, string newValue)
        {
            if (!IsEnabled)
                return;

            var sourceFlowBlockName = fieldElement?.Source?.Name;
            if (fieldElement?.IsPassword == true)
            {
                AppendProtocol(
                    "FieldValueChanged",
                    $"Field value change for '{fieldElement.FullyQualifiedName}' was not recorded because the field is marked as password.",
                    sourceFlowBlockName);
                return;
            }

            if (string.IsNullOrWhiteSpace(oldValue) && string.IsNullOrWhiteSpace(newValue))
            {
                AppendProtocol(
                    "FieldValueChanged",
                    $"Field value changed, old and new values are null or empty",
                    sourceFlowBlockName);

                return;
            }

            var config = Runtime.ExternalDebuggingInformation;
            var id = Interlocked.Increment(ref _nextFieldChangeId);

            lock (_sync)
            {
                if (_result == null)
                    return;

                _result.TotalFieldValueChanges++;

                var details = new RuntimeFieldValueChangeDetails
                {
                    Id = id,
                    Elapsed = GetElapsed(),
                    FullyQualifiedFieldName = fieldElement?.FullyQualifiedName ?? string.Empty,
                    SourceFlowBlockName = sourceFlowBlockName
                };

                if (_result.StoredFieldValueChanges < config.MaxCapturedFieldValueChanges)
                {
                    details.IsStored = true;
                    details.OldValue = oldValue;
                    details.NewValue = newValue;
                    _result.StoredFieldValueChanges++;
                }
                else
                {
                    details.IsStored = false;
                    details.Note = "Detailed values skipped due to MaxCapturedFieldValueChanges limit.";
                }

                _result.FieldValueChanges.Add(details);

                AppendProtocol(
                    "FieldValueChanged",
                    $"Field value changed, see id: {id}.",
                    sourceFlowBlockName,
                    id);
            }
        }

        private void AppendProtocol(
            string eventType,
            string message,
            string flowBlockName = null,
            int? fieldValueChangeId = null,
            int? generatedResultId = null,
            string details = null)
        {
            lock (_sync)
            {
                if (_result == null)
                    return;

                var entry = new RuntimeDebugProtocolEntry
                {
                    Elapsed = GetElapsed(),
                    EventType = eventType ?? string.Empty,
                    Message = message ?? string.Empty,
                    Details = details,
                    FlowBlockName = flowBlockName,
                    FieldValueChangeId = fieldValueChangeId,
                    GeneratedResultId = generatedResultId
                };
                _result.Protocol.Add(entry);
            }
        }

        private string GetElapsed()
        {
            if (_stopwatch == null)
                return "00:00.000";

            var elapsed = _stopwatch.Elapsed;
            return $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds:D3}";
        }

        private static bool IsTarget(string flowBlockName, string targetFlowBlockName)
        {
            if (string.IsNullOrWhiteSpace(flowBlockName) || string.IsNullOrWhiteSpace(targetFlowBlockName))
                return false;

            return string.Equals(flowBlockName, targetFlowBlockName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
