using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace FlowBlox.UICore.ViewModels
{
    public class ManageInputFilesViewModel : INotifyPropertyChanged
    {
        private const string DefaultInputFileNamePrefix = "new-file";
        private const string DefaultInputFileName = DefaultInputFileNamePrefix + ".txt";

        private readonly Window _ownerWindow;
        private readonly FlowBloxProject _project;
        private readonly bool _isReadOnly;

        private FlowBloxInputFile _selectedInputFile;

        public RelayCommand CloseCommand { get; }
        public RelayCommand OpenInputDirectoryCommand { get; }

        public RelayCommand CreateInputFileCommand { get; }
        public RelayCommand UploadInputFileCommand { get; }
        public RelayCommand DownloadInputFileCommand { get; }
        public RelayCommand OpenInputFileInExplorerCommand { get; }
        public RelayCommand RemoveInputFileCommand { get; }
        public RelayCommand ExecuteInputFileCommandCommand { get; }

        public ObservableCollection<FlowBloxInputFile> InputFiles { get; }
        public ObservableCollection<FlowBloxInputFileSyncMode> SyncModes { get; }

        public string ProjectInputDirectory => _project?.ProjectInputDirectory ?? "";
        public bool IsReadOnly => _isReadOnly;

        public FlowBloxInputFile SelectedInputFile
        {
            get => _selectedInputFile;
            set
            {
                if (ReferenceEquals(_selectedInputFile, value))
                    return;

                _selectedInputFile = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsInputFileSelected));
                InvalidateCommands();
            }
        }

        public bool IsInputFileSelected => SelectedInputFile != null;

        public ManageInputFilesViewModel(Window ownerWindow, FlowBloxProject project, bool isReadOnly = false)
        {
            _ownerWindow = ownerWindow;
            _project = project;
            _isReadOnly = isReadOnly;

            InputFiles = new ObservableCollection<FlowBloxInputFile>(_project?.InputFiles ?? new System.Collections.Generic.List<FlowBloxInputFile>());
            SyncModes = new ObservableCollection<FlowBloxInputFileSyncMode>(
                Enum.GetValues(typeof(FlowBloxInputFileSyncMode)).Cast<FlowBloxInputFileSyncMode>());

            CloseCommand = new RelayCommand(() => _ownerWindow?.Close());

            OpenInputDirectoryCommand = new RelayCommand(OpenInputDirectory);

            CreateInputFileCommand = new RelayCommand(CreateInputFile, () => !IsReadOnly);
            UploadInputFileCommand = new RelayCommand(UploadInputFile, () => IsInputFileSelected && !IsReadOnly);
            DownloadInputFileCommand = new RelayCommand(DownloadInputFile, () => IsInputFileSelected);
            OpenInputFileInExplorerCommand = new RelayCommand(OpenInputFileInExplorer, () => IsInputFileSelected);
            RemoveInputFileCommand = new RelayCommand(RemoveInputFiles, _ => IsInputFileSelected && !IsReadOnly);
            ExecuteInputFileCommandCommand = new RelayCommand(ExecuteSelectedInputFileCommand, () => IsInputFileSelected && !IsReadOnly);
        }

        private void OpenInputDirectory()
        {
            try
            {
                var dir = ProjectInputDirectory;
                if (string.IsNullOrWhiteSpace(dir))
                    return;

                Directory.CreateDirectory(dir);

                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_OpenInputDirectoryFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        private void CreateInputFile()
        {
            if (_project == null || IsReadOnly)
                return;

            var tpl = new FlowBloxInputFile
            {
                RelativePath = DefaultInputFileName,
                ContentBase64 = "",
                Command = "",
                ExecuteBeforeRuntime = false
            };

            InputFiles.Add(tpl);

            // Keep project list in sync (no explicit save button needed).
            _project.InputFiles ??= new System.Collections.Generic.List<FlowBloxInputFile>();
            _project.InputFiles.Add(tpl);

            SelectedInputFile = tpl;
        }

        private void UploadInputFile()
        {
            if (SelectedInputFile == null || IsReadOnly)
                return;

            var ofd = new OpenFileDialog
            {
                Filter = "All files (*.*)|*.*"
            };

            if (ofd.ShowDialog() != true)
                return;

            try
            {
                var bytes = File.ReadAllBytes(ofd.FileName);
                SelectedInputFile.ContentBytes = bytes;

                // Replace an empty or automatically generated file name with the uploaded file name.
                if (string.IsNullOrWhiteSpace(SelectedInputFile.FileName)
                    || SelectedInputFile.FileName.StartsWith(DefaultInputFileNamePrefix, StringComparison.OrdinalIgnoreCase))
                    SelectedInputFile.RelativePath = Path.GetFileName(ofd.FileName);

                OnPropertyChanged(nameof(InputFiles));
                OnPropertyChanged(nameof(SelectedInputFile));
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_UploadTemplateFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        private void DownloadInputFile()
        {
            if (SelectedInputFile == null)
                return;

            var defaultName = !string.IsNullOrWhiteSpace(SelectedInputFile.FileName) ? SelectedInputFile.FileName : "input-file.bin";

            var sfd = new SaveFileDialog
            {
                Filter = "All files (*.*)|*.*",
                FileName = defaultName
            };

            if (sfd.ShowDialog() != true)
                return;

            try
            {
                var bytes = SelectedInputFile.ContentBytes ?? Array.Empty<byte>();
                File.WriteAllBytes(sfd.FileName, bytes);

                ShowNotification(FlowBloxResourceUtil.GetLocalizedString("Message_TemplateDownloaded", typeof(Resources.ManageInputFilesWindow)));
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_DownloadTemplateFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        private void OpenInputFileInExplorer()
        {
            if (SelectedInputFile == null)
                return;

            try
            {
                var inputDir = ProjectInputDirectory;
                if (string.IsNullOrWhiteSpace(inputDir))
                    return;

                var targetPath = FlowBloxInputFileHelper.BuildAbsoluteTargetPath(inputDir, SelectedInputFile.RelativePath);

                if (!File.Exists(targetPath))
                {
                    ShowError(
                        FlowBloxResourceUtil.GetLocalizedString("Error_TemplateFileMissing", typeof(Resources.ManageInputFilesWindow)),
                        targetPath);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{targetPath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_OpenTemplateInExplorerFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        private void RemoveInputFiles(object selection)
        {
            if (_project == null || IsReadOnly)
                return;

            try
            {
                // Removing rows changes the grid's live selection, so capture every selected entry first.
                FlowBloxInputFile[] filesToRemove;
                if (selection is System.Collections.IEnumerable selectedItems)
                {
                    filesToRemove = selectedItems.OfType<FlowBloxInputFile>().ToArray();
                }
                else
                {
                    filesToRemove = SelectedInputFile == null
                        ? Array.Empty<FlowBloxInputFile>()
                        : new[] { SelectedInputFile };
                }

                foreach (var inputFile in filesToRemove)
                {
                    InputFiles.Remove(inputFile);
                    _project.InputFiles?.Remove(inputFile);
                }

                SelectedInputFile = null;

                // We do not delete the already materialized file automatically,
                // because it might contain user changes. Only the managed input file entry is removed.
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_RemoveTemplateFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        private void ExecuteSelectedInputFileCommand()
        {
            if (!IsReadOnly)
                ExecuteInputFileCommand(SelectedInputFile);
        }

        public void ExecuteInputFileCommand(FlowBloxInputFile inputFile)
        {
            if (inputFile == null)
                return;

            if (IsReadOnly)
                return;

            try
            {
                var rawCommand = inputFile.Command ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawCommand))
                {
                    ShowNotification(FlowBloxResourceUtil.GetLocalizedString("Message_InputFileCommandMissing", typeof(Resources.ManageInputFilesWindow)));
                    return;
                }

                var result = FlowBloxInputFileCommandExecutor.Execute(_project, inputFile);

                if (result.Success)
                {
                    ShowNotification(FlowBloxResourceUtil.GetLocalizedString("Message_InputFileCommandSuccess", typeof(Resources.ManageInputFilesWindow)));
                    return;
                }

                var details = !string.IsNullOrWhiteSpace(result.ExceptionMessage)
                    ? result.ExceptionMessage
                    : $"ExitCode={result.ExitCode}";

                if (!string.IsNullOrWhiteSpace(result.StandardError))
                    details += Environment.NewLine + result.StandardError;

                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_ExecuteInputFileCommandFailed", typeof(Resources.ManageInputFilesWindow)), details);
            }
            catch (Exception ex)
            {
                ShowError(FlowBloxResourceUtil.GetLocalizedString("Error_ExecuteInputFileCommandFailed", typeof(Resources.ManageInputFilesWindow)), ex.Message);
            }
        }

        public IReadOnlyList<FlowBloxInputFilePlaceholderElement> GetInputFilePlaceholderElements(FlowBloxInputFile inputFile)
        {
            return _project?.GetInputFilePlaceholderElements(inputFile) ?? Array.Empty<FlowBloxInputFilePlaceholderElement>();
        }

        private void ShowNotification(string message)
        {
            if (_ownerWindow is MetroWindow mw)
            {
                _ = MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Notification, message);
            }
        }

        private void ShowError(string titleOrMessage, string details)
        {
            if (_ownerWindow is MetroWindow mw)
            {
                _ = MessageBoxHelper.ShowMessageBoxAsync(mw, MessageBoxType.Error,
                    ApiErrorMessageHelper.BuildErrorMessage(titleOrMessage, details));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void InvalidateCommands()
        {
            CreateInputFileCommand?.Invalidate();
            UploadInputFileCommand?.Invalidate();
            DownloadInputFileCommand?.Invalidate();
            OpenInputFileInExplorerCommand?.Invalidate();
            RemoveInputFileCommand?.Invalidate();
            ExecuteInputFileCommandCommand?.Invalidate();
        }
    }
}
