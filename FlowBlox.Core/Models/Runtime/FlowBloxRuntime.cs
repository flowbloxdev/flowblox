using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.Core.Util;
using FlowBlox.Core.Exceptions;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Testing;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Registry;

namespace FlowBlox.Core.Models.Runtime
{
    public class FlowBloxRuntime : BaseRuntime
    {
        public bool IsNoDesignerMode { get; set; }

        public FlowBloxRuntime(FlowBloxProject project) : base(project)
        {
            this.StepTimeunit = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.RuntimeStepTimeunit].GetValueInt();
            this.StepwiseExecution = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.RuntimeDebuggingStepwiseExecution].Value.ToLower().Equals("true");
            this.StopOnWarning = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.RuntimeDebuggingStopOnWarning].Value.ToLower().Equals("true");
            this.StopOnError = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.RuntimeDebuggingStopOnError].Value.ToLower().Equals("true");
            this.AutoRestart = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.RuntimeAutoRestart].Value.ToLower().Equals("true");
        }

        private void ExecuteRequiredTestDefinitions()
        {
            var requiredTestDefinitions = ResolveRequiredTestDefinitions();
            if (requiredTestDefinitions.Count == 0)
                return;

            Report($"Run required test cases...");

            var registry = FlowBloxRegistryProvider.GetRegistry();
            var failedTests = new List<FlowBloxTestDefinition>();
            foreach (var testDefinition in requiredTestDefinitions)
            {
                var testExecutor = new FlowBloxTestExecutor();
                try
                {
                    testExecutor.ExpectationConditionFailed += TestExecutor_ExpectationConditionFailed;
                    testExecutor.Initialize(testDefinition, currentFlowBlock: null);
                    var testResult = testExecutor.ExecuteTest();
                    if (!testResult.Success)
                        failedTests.Add(testDefinition);
                }
                finally
                {
                    testExecutor.ExpectationConditionFailed -= TestExecutor_ExpectationConditionFailed;
                    testExecutor.Shutdown();
                }
            }

            if (!failedTests.Any())
            {
                Report($"All required test cases passed successfully ({requiredTestDefinitions.Count}).");
                return;
            }

            var failedDescriptions = failedTests.Select(testDefinition =>
            {
                var requiredFor = registry.GetFlowBlocks()
                    .Where(x => x.TestDefinitions.Contains(testDefinition))
                    .Select(x => x.Name)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var requiredForText = requiredFor.Any() ? string.Join(", ", requiredFor) : "n/a";
                return $"{testDefinition.Name} (required for: {requiredForText})";
            });

            var message = "The following required test cases failed: " + string.Join("; ", failedDescriptions);
            Report(message, FlowBloxLogLevel.Error);
            throw new InvalidOperationException(message);
        }

        private static List<FlowBloxTestDefinition> ResolveRequiredTestDefinitions()
        {
            return FlowBloxRegistryProvider.GetRegistry()
                .GetManagedObjects<FlowBloxTestDefinition>()
                .Where(x => x.RequiredForExecution)
                .OrderBy(x => x.Name)
                .ToList();
        }

        private void TestExecutor_ExpectationConditionFailed(object? sender, TestExpectationConditionFailedEventArgs e)
        {
            var flowBlock = e.FlowBlock;
            if (flowBlock == null || !flowBlock.GenerationStrategies.Any())
                return;

            Report(
                $"Expectation failed in test \"{e.TestDefinition?.Name}\" at flow block \"{flowBlock.Name}\". Trying regeneration.",
                FlowBloxLogLevel.Info);

            var regenerationExecutor = new FlowBlockRuntimeRegenerationExecutor(flowBlock);
            regenerationExecutor.LogCreated += RegenerationExecutor_LogCreated;
            var regenerationSuccessful = regenerationExecutor.ExecuteRegeneration(
                e.Runtime,
                e.TestDefinition,
                e.CurrentResult);
            regenerationExecutor.LogCreated -= RegenerationExecutor_LogCreated;

            e.RepeatLatestExecution = regenerationSuccessful;
        }

        private void RegenerationExecutor_LogCreated(object? sender, LogCreatedEventArgs e) => Report(e.Message, e.LogLevel);

        protected override void OnBeforeRuntimeStarted(BaseFlowBlock startFlowBlock, IEnumerable<BaseFlowBlock> flowBlocks, IEnumerable<IManagedObject> managedObjects)
        {
            ExecuteInputFileStartupCommands();
            ExecuteRequiredTestDefinitions();

            base.OnBeforeRuntimeStarted(startFlowBlock, flowBlocks, managedObjects);
        }

        public override void HandlePause()
        {
            if (!IsNoDesignerMode)
            {
                bool pauseContinue = false;
                if (this.Pause)
                {
                    pauseContinue = true;
                    this.RaisePauseContinue(this.Pause);
                }

                while (this.Pause && !this.Aborted)
                {
                    Thread.Sleep(1000);
                }

                if (this.Aborted)
                {
                    this.Report("The execution has been cancelled by user.");
                    throw new RuntimeCancellationException();
                }

                if (pauseContinue)
                    this.RaisePauseContinue(this.Pause);
            }

            base.HandlePause();
        }

        private static readonly HashSet<string> _alreadyCreatedLogfileNames = new HashSet<string>();
        private static readonly object _logfileLock = new object();
        private string _runtimeLogFileName;

        public string RuntimeLogFileName
        {
            get
            {
                if (string.IsNullOrEmpty(_runtimeLogFileName))
                {
                    var baseName = string.Join("_",
                        Started.ToString("dd-MM-yyyy_HH-mm-ss"),
                        IOUtil.GetValidFileName(Project.ProjectName),
                        "runtime");

                    _runtimeLogFileName = baseName;

                    lock (_logfileLock)
                    {
                        var runtimeLogDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsRuntimeLogDir).Value;
                        if (_alreadyCreatedLogfileNames.Contains(_runtimeLogFileName) ||
                            File.Exists(Path.Combine(runtimeLogDirectory, _runtimeLogFileName)))
                        {
                            int suffix = 1;
                            while (_alreadyCreatedLogfileNames.Contains(_runtimeLogFileName + $"_{suffix}") ||
                                File.Exists(Path.Combine(runtimeLogDirectory, _runtimeLogFileName + $"_{suffix}")))
                            {
                                suffix++;
                            }
                            _runtimeLogFileName += $"_{suffix}";
                        }
                        _alreadyCreatedLogfileNames.Add(_runtimeLogFileName);
                    }
                }
                return _runtimeLogFileName;
            }
        }

        public string GetLogfilePath() => _logger?.GetLogfilePath();

        private ILogger _logger;
        public override void Report(string message, FlowBloxLogLevel logLevel = FlowBloxLogLevel.Info, Exception e = null)
        {
            if (_logger == null)
                _logger = new FlowBloxRuntimeLogger(this.RuntimeLogFileName);

            if (logLevel == FlowBloxLogLevel.Info)
                _logger.Info(message);
            else if (logLevel == FlowBloxLogLevel.Warning)
                _logger.Warn(message);
            else if (logLevel == FlowBloxLogLevel.Error)
            {
                if (!string.IsNullOrEmpty(message))
                {
                    _logger.Error(message, e);
                }
                else if (e != null)
                {
                    _logger.Exception(e);
                }
            }

            base.Report(message, logLevel, e);
        }

        public void Execute()
        {
            try
            {
                this.Running = true;
                this.NotifyBeforeRuntimeStarted();
                this.OnBeforeRuntimeStarted();
                this.NotifyRuntimeStarted();
                this.Report("StepwiseExecution: " + StepwiseExecution.ToString().ToLower());
                this.Report("StepTimeunit: " + StepTimeunit.ToString());
                this.Report("AutoRestart: " + AutoRestart.ToString());

                int executionCounter = 0;
                do
                {
                    if (executionCounter == 0)
                    {
                        this.Report("Runtime execution has been started.");
                    }
                    else
                    {
                        this.Report($"Execution has been restarted. Number of executions: {executionCounter + 1}");
                    }
                    this.TaskRunner.Run(StartFlowBlock);
                    executionCounter++;
                }
                while (!Aborted && AutoRestart);

                this.Report("Runtime execution has finished.");
                this.Running = false;
                this.OnAfterRuntimeFinished();
                this.NotifyRuntimeFinished();
            }
            catch (RuntimeCancellationException e)
            {
                this.Aborted = true;
                this.Running = false;
                this.OnAfterRuntimeFinished();
                this.NotifyRuntimeFinished();
            }
            catch (Exception e)
            {
                this.Aborted = true;
                this.Running = false;
                this.NotifyRuntimeAborted(e);
                this.Report("Runtime execution has aborted due to an critical error.", FlowBloxLogLevel.Error, e);
                this.OnAfterRuntimeFinished();
                this.NotifyRuntimeFinished();
            }
        }
    }
}


