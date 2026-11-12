using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.FlowBlocks;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Models.Runtime.Debugging;
using FlowBlox.Core.Validations;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using System.ComponentModel.DataAnnotations;

namespace FlowBlox.Core.Models.Runtime
{
    public abstract partial class BaseRuntime : IDisposable
    {
        public delegate void LogMessageCreatedEventHandler(BaseRuntime runtime, string message, FlowBloxLogLevel logLevel);
        internal delegate void LogEntryCreatedEventHandler(BaseRuntime runtime, string message, FlowBloxLogLevel logLevel, Exception exception);
        public delegate void FinishedEventHandler(object result);
        public delegate void RuntimeStartedEventHandler(BaseRuntime runtime);
        public delegate void FocusChangedEventHandler(BaseFlowBlock flowBlock);
        public delegate void PauseEventHandler(bool isPaused);

        public event LogMessageCreatedEventHandler LogMessageCreated;
        internal event LogEntryCreatedEventHandler LogEntryCreated;
        public event RuntimeStartedEventHandler RuntimeStarted;
        public event FinishedEventHandler Finish;
        public event FocusChangedEventHandler FocusChanged;
        public event PauseEventHandler PauseContinue;

        public bool Pause { get; set; }
        public bool Aborted { get; set; }
        public bool Running { get; set; }
        public bool AutoRestart { get; set; }
        public bool StepwiseExecution { get; set; }
        public bool StopOnWarning { get; set; }
        public bool StopOnError { get; set; }
        public int StepTimeunit { get; set; }
        public int? DebuggingStepTimeunit { get; set; }
        public int EffectiveStepTimeunit => DebuggingStepTimeunit.GetValueOrDefault() > 0
            ? DebuggingStepTimeunit!.Value
            : StepTimeunit;
        public bool ExecutionFlowEnabled { get; set; }
        internal int ExecutionLayer { get; private set; }
        public bool DisableInterceptors { get; set; }
        public RuntimeExternalDebuggingInformation ExternalDebuggingInformation { get; set; }

        private readonly IEnumerable<IRuntimeInterceptor> _interceptors;

        public IEnumerable<IRuntimeInterceptor> Interceptors => _interceptors;

        internal delegate void FieldChangeHandler(FieldElement fieldElement);

        internal event FieldChangeHandler FieldChanged;

        public DateTime Started { get; }

        private FlowBloxProject _project;
        public FlowBloxProject Project
        {
            get => _project;
            protected set
            {
                _project = value;

                foreach (var interceptor in _interceptors)
                {
                    interceptor.Project = _project;
                    interceptor.Runtime = this;
                }
            }
        }

        public BaseFlowBlock StartFlowBlock { get; protected set; }

        private RuntimeTaskRunner _runner;

        public RuntimeTaskRunner TaskRunner => _runner ??= new RuntimeTaskRunner(this);

        internal void IncreaseExecutionLayer() => ExecutionLayer++;

        internal void DecreaseExecutionLayer()
        {
            if (ExecutionLayer == 0)
                return;

            ExecutionLayer--;
        }

        protected BaseRuntime(FlowBloxProject project)
        {
            this.StartFlowBlock = FlowBloxRegistryProvider.GetRegistry().GetStartFlowBlock();

            if (StartFlowBlock == null)
                throw new Exception("You must define a valid start element.");

            _interceptors = FlowBloxServiceLocator.Instance.GetServices<IRuntimeInterceptor>();

            this.Project = project;
            this.ExecutionFlowEnabled = true;
            this.Started = DateTime.Now;
        }

        public virtual void Report(string message, FlowBloxLogLevel logLevel = FlowBloxLogLevel.Info, Exception e = null)
        {
            LogEntryCreated?.Invoke(this, message, logLevel, e);
            LogMessageCreated?.Invoke(this, message, logLevel);

            if (e != null)
            {
                LogMessageCreated?.Invoke(this, "The exception is logged below.", FlowBloxLogLevel.Error);
                LogMessageCreated?.Invoke(this, e.ToString(), FlowBloxLogLevel.Error);
            }
        }

        internal void NotifyFieldChange(FieldElement fieldElement, string oldValue, string newValue)
        {
            FieldChanged?.Invoke(fieldElement);

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyFieldChange(fieldElement);
                    interceptor.NotifyFieldChange(fieldElement, oldValue, newValue);
                }
            }

            HandlePause();
        }

        protected void NotifyBeforeRuntimeStarted()
        {
            if (DisableInterceptors)
                return;

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyBeforeRuntimeStarted();
            }
        }

        protected void NotifyRuntimeStarted()
        {
            RuntimeStarted?.Invoke(this);

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyRuntimeStarted();
                }
            }
        }

        protected void NotifyRuntimeFinished()
        {
            Finish?.Invoke(this);

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyRuntimeFinished();
                }
            }
        }

        protected void NotifyRuntimeAborted(Exception exception)
        {
            if (DisableInterceptors)
                return;

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyRuntimeAborted(exception);
            }
        }

        internal void NotifyInvocationStarted(BaseFlowBlock flowBlock)
        {
            Report($"Invocation started for flow block: \"{flowBlock.Name}\"", FlowBloxLogLevel.Info);

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyInvocationStarted(flowBlock);
                }
            }
        }

        internal void NotifyInvocationFinished(BaseFlowBlock flowBlock)
        {
            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyInvocationFinished(flowBlock);
                }
            }

            Report($"Invocation finished for flow block: \"{flowBlock.Name}\"", FlowBloxLogLevel.Info);
        }

        internal void NotifyBeforeFlowBlockValidation(BaseFlowBlock flowBlock)
        {
            if (DisableInterceptors)
                return;

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyBeforeFlowBlockValidation(flowBlock);
            }
        }

        internal void NotifyPreconditionsNotMet(BaseFlowBlock flowBlock, IReadOnlyList<string> messages)
        {
            if (DisableInterceptors)
                return;

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyPreconditionsNotMet(flowBlock, messages ?? Array.Empty<string>());
            }
        }

        internal void NotifyIterationStarted(BaseFlowBlock flowBlock)
        {
            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyIterationStarted(flowBlock);
                }
            }
        }

        internal void NotifyIterationFinished(BaseFlowBlock flowBlock)
        {
            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyIterationFinished(flowBlock);
                }
            }
        }

        internal void NotifyResultDatasetsGenerated(BaseResultFlowBlock flowBlock, IReadOnlyList<FlowBlockOutDataset> resultDatasets)
        {
            if (!DisableInterceptors)
            {
                var summary = new RuntimeResultDatasetSummary(flowBlock, resultDatasets ?? Array.Empty<FlowBlockOutDataset>());
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyResultDatasetsGenerated(summary);
                }
            }
        }

        internal void NotifyBeforeInputProcessing(
            BaseFlowBlock flowBlock,
            int inputDatasetsCount,
            IReadOnlyDictionary<string, Enum> inputBehaviours,
            BaseFlowBlock iterationContext)
        {
            if (DisableInterceptors)
                return;

            var summary = new RuntimeInputProcessingSummary(
                flowBlock: flowBlock,
                iterationContext: iterationContext,
                inputDatasetCount: inputDatasetsCount,
                inputBehaviours: inputBehaviours ?? new Dictionary<string, Enum>());

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyBeforeInputProcessing(summary);
            }
        }

        public void NotifyWarning(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            if (StopOnWarning)
                Pause = true;

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyWarning(baseFlowBlock, message, exception);
                }
            }
        }

        public void NotifyError(BaseFlowBlock baseFlowBlock, string message, Exception exception = null)
        {
            if (StopOnError)
                Pause = true;

            if (!DisableInterceptors)
            {
                foreach (var interceptor in _interceptors)
                {
                    interceptor.NotifyError(baseFlowBlock, message, exception);
                }
            }
        }

        public void Focus(BaseFlowBlock baseGridElement)
        {
            if (FocusChanged != null)
                FocusChanged(baseGridElement);
        }

        protected void RaisePauseContinue(bool pause)
        {
            PauseContinue?.Invoke(pause);
        }

        private readonly HashSet<BaseFlowBlock> _initializedFlowBlocks = new HashSet<BaseFlowBlock>();

        private void InitializeFlowBlock(BaseFlowBlock flowBlock)
        {
            if (flowBlock.ReferencedFlowBlocks.All(refBlock => _initializedFlowBlocks.Contains(refBlock)))
            {
                if (!_initializedFlowBlocks.Contains(flowBlock))
                {
                    flowBlock.RuntimeStarted(this);
                    _initializedFlowBlocks.Add(flowBlock);
                    flowBlock.GetNextFlowBlocks().ForEach(x => InitializeFlowBlock(x));
                }
            }
        }

        protected virtual void OnBeforeRuntimeStarted(BaseFlowBlock startFlowBlock, IEnumerable<BaseFlowBlock> flowBlocks, IEnumerable<IManagedObject> managedObjects)
        {
            ExecuteInputFileStartupCommands();
            Report($"Initializing runtime for managed objects...");
            foreach (var managedObject in managedObjects)
            {
                managedObject.RuntimeStarted(this);
            }
            Report($"Initializing runtime for flow blocks...");
            InitializeFlowBlock(startFlowBlock);
            var notInitialized = flowBlocks.Except(flowBlocks.OfType<NoteFlowBlock>()).Where(x => !_initializedFlowBlocks.Contains(x));
            if (notInitialized.Any())
            {
                var notInitializedNames = string.Join(", ", notInitialized.Select(x => x.Name));
                throw new InvalidOperationException($"The following FlowBlocks have not been initialized: {notInitializedNames}. Please check their dependencies and ensure they are correctly configured to allow proper initialization.");
            }
            Report($"Runtime initialization completed.");
        }

        protected virtual bool ShouldExecuteInputFileStartupCommands() => true;
        private bool _inputFileStartupCommandsExecuted;

        protected void ExecuteInputFileStartupCommands()
        {
            if (_inputFileStartupCommandsExecuted)
                return;

            _inputFileStartupCommandsExecuted = true;

            if (!ShouldExecuteInputFileStartupCommands())
                return;

            if (Project?.InputFiles == null || Project.InputFiles.Count == 0)
                return;

            // Keep managed input files in sync before optional startup commands are executed.
            FlowBloxInputFileHelper.SynchronizeInputFiles(Project);

            var inputFilesWithCommands = Project.InputFiles
                .Where(x => x != null
                    && x.ExecuteBeforeRuntime
                    && !string.IsNullOrWhiteSpace(x.Command))
                .ToList();

            if (!inputFilesWithCommands.Any())
                return;

            Report("Executing input file startup commands...");

            foreach (var inputFile in inputFilesWithCommands)
            {
                var relativePath = FlowBloxInputFileHelper.NormalizeRelativePath(inputFile.RelativePath ?? string.Empty);
                var result = FlowBloxInputFileCommandExecutor.Execute(
                    Project,
                    inputFile,
                    GetCancellationToken(),
                    inputFileAlreadyEnsured: true,
                    onCommandResolved: command =>
                        Report($"Executing startup command for input file \"{relativePath}\": {command}"));

                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                    Report($"Startup command output: {TruncateForReport(result.StandardOutput)}");

                if (!string.IsNullOrWhiteSpace(result.StandardError))
                    Report($"Startup command errors: {TruncateForReport(result.StandardError)}", FlowBloxLogLevel.Warning);

                if (!result.Success)
                {
                    var errorText = !string.IsNullOrWhiteSpace(result.ExceptionMessage)
                        ? result.ExceptionMessage
                        : $"ExitCode={result.ExitCode}";

                    throw new InvalidOperationException(
                        $"Startup command failed for input file \"{relativePath}\" ({errorText}).");
                }

                Report($"Startup command finished successfully (ExitCode={result.ExitCode}).");
            }
        }

        private static string TruncateForReport(string value, int maxLength = 1000)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            if (value.Length <= maxLength)
                return value.Trim();

            return value[..maxLength].Trim() + "...";
        }

        protected virtual void OnAfterRuntimeFinished(IEnumerable<BaseFlowBlock> flowBlocks, IEnumerable<IManagedObject> managedObjects)
        {
            Report($"Finishing runtime for managed objects...");
            foreach (var managedObject in managedObjects)
            {
                managedObject.RuntimeFinished(this);
            }
            Report($"Finishing runtime for flow blocks...");
            foreach (var flowBlock in flowBlocks)
            {
                flowBlock.RuntimeFinished(this);
            }
            Report($"Finishing runtime completed.");
        }

        private void IntegrityCheck(IEnumerable<BaseFlowBlock> flowBlocks, IEnumerable<FieldElement> userFields)
        {
            HashSet<FieldElement> fieldElements = new HashSet<FieldElement>();
            foreach (var resultFlowBlock in flowBlocks.OfType<BaseResultFlowBlock>())
            {
                fieldElements.AddRange(resultFlowBlock.Fields);
            }

            foreach (var flowBlock in flowBlocks)
            {
                foreach (var dependendField in flowBlock.GetAssociatedFields())
                {
                    if (dependendField.UserField)
                    {
                        if (!userFields.Contains(dependendField))
                            throw new InvalidOperationException($"Referenced field \"{dependendField.FullyQualifiedName}\" used by \"{flowBlock.Name}\" could not be determined in the user field elements.");
                    }
                    else
                    {
                        if (!fieldElements.Contains(dependendField))
                            throw new InvalidOperationException($"Referenced field \"{dependendField.FullyQualifiedName}\" used by \"{flowBlock.Name}\" could not be determined in the field elements of the initialized FlowBlocks.");
                    }
                }
            }
        }

        private void ValidateFlowBlocks(BaseFlowBlock startFlowBlock)
        {
            if (startFlowBlock == null)
                return;

            var invalidBlocks = new Dictionary<BaseFlowBlock, List<string>>();
            var visited = new HashSet<BaseFlowBlock>();

            ValidateFlowBlocksRecursive(startFlowBlock, visited, invalidBlocks);

            if (!invalidBlocks.Any())
                return;

            var errorMessage = "Validation errors occurred:";
            foreach (var kvp in invalidBlocks)
            {
                errorMessage += $"\n\nFlowBlock: {kvp.Key.Name}";
                foreach (var validationMessage in kvp.Value)
                {
                    errorMessage += $"\n  - {validationMessage}";
                }
            }

            throw new InvalidOperationException(errorMessage);
        }

        private void ValidateFlowBlocksRecursive(
            BaseFlowBlock flowBlock,
            HashSet<BaseFlowBlock> visited,
            Dictionary<BaseFlowBlock, List<string>> invalidBlocks)
        {
            ValidateFlowBlocksRecursive(flowBlock, visited, invalidBlocks, out _);
        }

        private void ValidateFlowBlocksRecursive(
            BaseFlowBlock flowBlock,
            HashSet<BaseFlowBlock> visited,
            Dictionary<BaseFlowBlock, List<string>> invalidBlocks,
            out bool validationCancelled)
        {
            validationCancelled = false;

            if (flowBlock == null)
                return;

            if (!visited.Add(flowBlock))
                return;

            if (ShouldCancelValidation(flowBlock, false))
            {
                validationCancelled = true;
                return;
            }

            if (!ShouldSkipValidation(flowBlock))
            {
                var results = FlowBloxComponentValidator.Validate(flowBlock);

                if (results.Any())
                    invalidBlocks[flowBlock] = results;
            }

            if (ShouldCancelValidation(flowBlock, true))
            {
                validationCancelled = true;
                return;
            }

            foreach (var nextFlowBlock in flowBlock.GetNextFlowBlocks())
            {
                ValidateFlowBlocksRecursive(nextFlowBlock, visited, invalidBlocks, out validationCancelled);
                if (validationCancelled)
                    return;
            }
        }

        protected virtual bool ShouldCancelValidation(BaseFlowBlock flowBlock, bool validationFinished)
        {
            if (DisableInterceptors)
                return false;

            return _interceptors.Any(x => x.ShouldCancelValidation(flowBlock, validationFinished));
        }

        protected virtual bool ShouldSkipValidation(BaseFlowBlock flowBlock)
        {
            return false;
        }

        protected virtual void OnBeforeRuntimeStarted(IEnumerable<BaseFlowBlock> flowBlocks)
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();

            var startFlowBlock = registry.GetStartFlowBlock();
            var managedObjects = registry.GetManagedObjects();
            var userFields = registry.GetUserFields();

            if (ShouldValidateUserFieldsAtRuntimeStartup())
                ValidateUserFieldsAtRuntimeStartup();

            this.OnBeforeRuntimeStarted(startFlowBlock, flowBlocks, managedObjects);
            this.IntegrityCheck(flowBlocks, userFields);
            this.ValidateFlowBlocks(startFlowBlock);
        }

        protected virtual bool ShouldValidateUserFieldsAtRuntimeStartup() => true;

        /// <summary>
        /// Checks effective user-field inputs before any flow or startup command runs.
        /// Test execution calls this after applying its runtime-bound input values.
        /// </summary>
        public void ValidateUserFieldsAtRuntimeStartup()
        {
            var errors = new List<string>();
            foreach (var field in Project.FlowBloxRegistry.GetUserFields()
                .Where(x => x.UserFieldType is UserFieldTypes.Input || x.UserFieldType is UserFieldTypes.Memory))
            {
                var clearsValue = field.UserFieldType == UserFieldTypes.Memory && field.ClearOnRuntimeStart;
                var value = clearsValue
                    ? string.Empty
                    : FlowBloxFieldHelper.ResolveFieldValue(field);

                if (field.IsRequiredAtRuntimeStartup && string.IsNullOrWhiteSpace(value))
                {
                    var key = clearsValue
                        ? "FieldElement_RequiredAndClearedAtStartup"
                        : "FieldElement_RequiredValueMissing";
                    errors.Add(string.Format(
                        FlowBloxResourceUtil.GetLocalizedString(key),
                        field.FullyQualifiedName));
                }
                else if (!string.IsNullOrWhiteSpace(value)
                    && field.LoVType == LoVType.PossibleValues
                    && field.ListOfValues?.Count > 0
                    && !field.ListOfValues.Any(x => string.Equals(x.Value, value, StringComparison.Ordinal)))
                {
                    errors.Add(string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("FieldElement_ValueNotAllowed"),
                        field.FullyQualifiedName));
                }
            }

            if (errors.Count > 0)
                throw new ValidationException(string.Join(Environment.NewLine, errors));
        }

        protected virtual void OnBeforeRuntimeStarted()
        {
            InitDefaultOptions();
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var flowBlocks = registry.GetFlowBlocks();
            OnBeforeRuntimeStarted(flowBlocks);
        }

        private void InitDefaultOptions()
        {
            var options = FlowBloxOptions.GetOptionInstance();
            options.InitDefaults(false);
        }

        protected virtual void OnAfterRuntimeFinished(IEnumerable<BaseFlowBlock> flowBlocks)
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var managedObjects = registry.GetManagedObjects();
            this.OnAfterRuntimeFinished(flowBlocks, managedObjects);
        }

        protected virtual void OnAfterRuntimeFinished()
        {
            var registry = FlowBloxRegistryProvider.GetRegistry();
            var flowBlocks = registry.GetFlowBlocks();
            OnAfterRuntimeFinished(flowBlocks);
        }

        public virtual void HandlePause()
        {
            
        }

        public void Dispose()
        {

        }

        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly object _cancellationSync = new object();

        public RuntimeCancellationContext CancellationContext { get; private set; }

        /// <summary>
        /// Returns a CancellationToken that can be used by FlowBlocks and external services.
        /// </summary>
        public virtual CancellationToken GetCancellationToken()
        {
            return _cancellationTokenSource.Token;
        }

        public virtual void CancelExecution(RuntimeCancellationKind cancellationKind, string reason)
        {
            lock (_cancellationSync)
            {
                if (Aborted)
                    return;

                Aborted = true;
                CancellationContext = new RuntimeCancellationContext
                {
                    UtcTimestamp = DateTime.UtcNow,
                    CancellationKind = cancellationKind,
                    Reason = reason ?? string.Empty
                };

                try
                {
                    _cancellationTokenSource.Cancel();
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error(
                        "Failed to cancel the runtime cancellation token source during runtime shutdown.",
                        ex);
                }

                TaskRunner.CancelPendingWorkItems();
                NotifyRuntimeCancelled(CancellationContext);
            }
        }

        private void NotifyRuntimeCancelled(RuntimeCancellationContext cancellationContext)
        {
            if (DisableInterceptors || cancellationContext == null)
                return;

            foreach (var interceptor in _interceptors)
            {
                interceptor.NotifyRuntimeCancelled(cancellationContext);
            }
        }
    }
}
