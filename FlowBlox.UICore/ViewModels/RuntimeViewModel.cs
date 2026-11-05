using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Resources;
using FlowBlox.UICore.Utilities;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;

namespace FlowBlox.UICore.ViewModels
{
    public class RuntimeViewModel : INotifyPropertyChanged, IDisposable
    {
        private const int DebuggingStepTimeunitMinimum = 20;
        private const int DebuggingStepTimeunitMaximum = 1000;
        private readonly IRuntimeStateService _runtimeStateService;
        private readonly IFlowBloxMessageBoxService _messageBoxService;
        private BaseRuntime _runtime;
        private bool _initialized;
        private bool _stepwiseExecution;
        private bool _stopOnWarning;
        private bool _stopOnError;
        private int _debuggingStepTimeunit;

        public RuntimeViewModel()
        {
            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();

            PauseCommand = new RelayCommand(PauseExecutionByUser, () => CanPause);
            ContinueCommand = new RelayCommand(ContinueExecutionByUser, () => CanContinue);
            StopCommand = new RelayCommand(StopExecutionByUser, () => CanStop);
            OpenLogFileCommand = new RelayCommand(OpenLogFile, () => CanOpenLogFile);
            RefreshCommand = new RelayCommand(UpdateRuntimeState);
            OpenExportDirectoryCommand = new RelayCommand(OpenExportDirectory);

            _runtimeStateService.StateChanged += RuntimeStateService_StateChanged;
            InitializeRuntimeSettings();
            InitializeRuntime(_runtimeStateService.CurrentRuntime);
        }

        public ICommand PauseCommand { get; }
        public ICommand ContinueCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OpenLogFileCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand OpenExportDirectoryCommand { get; }

        public bool StepwiseExecution
        {
            get => _stepwiseExecution;
            set
            {
                if (_stepwiseExecution == value)
                    return;

                _stepwiseExecution = value;
                OnPropertyChanged(nameof(StepwiseExecution));
                ApplyBooleanRuntimeOption(OptionsConstants.RuntimeDebuggingStepwiseExecution, value, runtime => runtime.StepwiseExecution = value);
            }
        }

        public bool StopOnWarning
        {
            get => _stopOnWarning;
            set
            {
                if (_stopOnWarning == value)
                    return;

                _stopOnWarning = value;
                OnPropertyChanged(nameof(StopOnWarning));
                ApplyBooleanRuntimeOption(OptionsConstants.RuntimeDebuggingStopOnWarning, value, runtime => runtime.StopOnWarning = value);
            }
        }

        public bool StopOnError
        {
            get => _stopOnError;
            set
            {
                if (_stopOnError == value)
                    return;

                _stopOnError = value;
                OnPropertyChanged(nameof(StopOnError));
                ApplyBooleanRuntimeOption(OptionsConstants.RuntimeDebuggingStopOnError, value, runtime => runtime.StopOnError = value);
            }
        }

        public bool CanPause => _runtime?.Running == true && !_runtime.Pause;
        public bool CanContinue => _runtime?.Running == true && _runtime.Pause;
        public bool CanStop => _runtime?.Running == true && !_runtime.Aborted;
        public bool CanOpenLogFile => _runtime?.Running == true && !_runtime.Aborted && _runtime is FlowBloxRuntime;

        public double DebuggingStepTimeunit
        {
            get => _debuggingStepTimeunit;
            set
            {
                var stepTimeunit = CoerceDebuggingStepTimeunit((int)Math.Round(value));
                if (_debuggingStepTimeunit == stepTimeunit)
                    return;

                _debuggingStepTimeunit = stepTimeunit;
                OnPropertyChanged(nameof(DebuggingStepTimeunit));
                OnPropertyChanged(nameof(DebuggingStepTimeunitText));
                ApplyIntegerRuntimeOption(OptionsConstants.RuntimeDebuggingStepTimeunit, stepTimeunit, runtime => runtime.DebuggingStepTimeunit = stepTimeunit);
            }
        }

        public string DebuggingStepTimeunitText
        {
            get
            {
                if (_debuggingStepTimeunit >= 1000)
                    return $"{_debuggingStepTimeunit / 1000d:0.#} s";

                return $"{_debuggingStepTimeunit} ms";
            }
        }

        public void InitializeRuntime(BaseRuntime runtime)
        {
            InitializeRuntimeSettings();
            _runtime = runtime;
            ApplyCurrentSettingsToRuntime();
            UpdateRuntimeState();
        }

        public void ContinueExecutionByUser()
        {
            if (!CanContinue)
                return;

            _runtime.Pause = false;
            _runtimeStateService?.AttachRuntime(_runtime);
            UpdateRuntimeState();
        }

        public void PauseExecutionByUser()
        {
            if (!CanPause)
                return;

            _runtime.Pause = true;
            _runtimeStateService?.AttachRuntime(_runtime);
            UpdateRuntimeState();
        }

        public void StopExecutionByUser()
        {
            if (!CanStop)
                return;

            _runtime.Aborted = true;
            _runtimeStateService?.AttachRuntime(_runtime);
            UpdateRuntimeState();
        }

        private void InitializeRuntimeSettings()
        {
            _initialized = false;

            _stepwiseExecution = GetBooleanOption(OptionsConstants.RuntimeDebuggingStepwiseExecution);
            _stopOnWarning = GetBooleanOption(OptionsConstants.RuntimeDebuggingStopOnWarning);
            _stopOnError = GetBooleanOption(OptionsConstants.RuntimeDebuggingStopOnError);
            _debuggingStepTimeunit = CoerceDebuggingStepTimeunit(GetIntegerOption(OptionsConstants.RuntimeDebuggingStepTimeunit));

            OnPropertyChanged(nameof(StepwiseExecution));
            OnPropertyChanged(nameof(StopOnWarning));
            OnPropertyChanged(nameof(StopOnError));
            OnPropertyChanged(nameof(DebuggingStepTimeunit));
            OnPropertyChanged(nameof(DebuggingStepTimeunitText));

            _initialized = true;
        }

        private static bool GetBooleanOption(string optionName)
        {
            var option = FlowBloxOptions.GetOptionInstance().OptionCollection[optionName];
            return bool.TryParse(option?.Value, out var value) && value;
        }

        private static int GetIntegerOption(string optionName)
        {
            var option = FlowBloxOptions.GetOptionInstance().OptionCollection[optionName];
            return int.TryParse(option?.Value, out var value) ? value : 0;
        }

        private void ApplyBooleanRuntimeOption(string optionName, bool value, Action<BaseRuntime> applyToRuntime)
        {
            if (!_initialized)
                return;

            if (_runtime != null)
                applyToRuntime(_runtime);

            FlowBloxOptions.GetOptionInstance().OptionCollection[optionName].Value = value.ToString().ToLower();
            FlowBloxOptions.GetOptionInstance().Save();
        }

        private void ApplyIntegerRuntimeOption(string optionName, int value, Action<BaseRuntime> applyToRuntime)
        {
            if (!_initialized)
                return;

            if (_runtime != null)
                applyToRuntime(_runtime);

            FlowBloxOptions.GetOptionInstance().OptionCollection[optionName].Value = value.ToString();
            FlowBloxOptions.GetOptionInstance().Save();
        }

        private void ApplyCurrentSettingsToRuntime()
        {
            if (_runtime == null)
                return;

            _runtime.StepwiseExecution = StepwiseExecution;
            _runtime.StopOnWarning = StopOnWarning;
            _runtime.StopOnError = StopOnError;
            _runtime.DebuggingStepTimeunit = _debuggingStepTimeunit;
        }

        private static int CoerceDebuggingStepTimeunit(int value)
        {
            if (value <= 0)
                return 0;

            return Math.Clamp(value, DebuggingStepTimeunitMinimum, DebuggingStepTimeunitMaximum);
        }

        private void OpenLogFile()
        {
            if (_runtime is not FlowBloxRuntime flowBloxRuntime)
                return;

            var logfilePath = flowBloxRuntime.GetLogfilePath();
            if (File.Exists(logfilePath))
            {
                FlowBloxEditingHelper.OpenUsingEditor(logfilePath);
                return;
            }

            _messageBoxService?.ShowMessageBox(
                RuntimeView.Message_LogFileNotFound,
                RuntimeView.Title_OpenLogFileFailed,
                FlowBloxMessageBoxTypes.Warning);
        }

        private void OpenExportDirectory()
        {
            try
            {
                var outputDirectory = FlowBloxOptions.GetOptionInstance().OptionCollection[OptionsConstants.PathsOutputDir].Value;
                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                Process.Start("explorer.exe", $"\"{outputDirectory}\"");
            }
            catch (Exception ex)
            {
                _messageBoxService?.ShowMessageBox(
                    string.Format(RuntimeView.Message_OpenExportDirectoryFailed, ex.Message),
                    RuntimeView.Title_OpenExportDirectoryFailed,
                    FlowBloxMessageBoxTypes.Error);
            }
        }

        private void RuntimeStateService_StateChanged(object sender, Events.RuntimeStateChangedEventArgs e)
        {
            _runtime = e.Runtime;
            UpdateRuntimeState();
        }

        private void UpdateRuntimeState()
        {
            OnPropertyChanged(nameof(CanPause));
            OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(CanOpenLogFile));
            CommandManager.InvalidateRequerySuggested();
        }

        public void Dispose()
        {
            if (_runtimeStateService != null)
                _runtimeStateService.StateChanged -= RuntimeStateService_StateChanged;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
