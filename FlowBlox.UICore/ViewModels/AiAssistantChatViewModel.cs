using FlowBlox.Core.Constants;
using FlowBlox.AIAssistant.History;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.Resources;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Logging;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;

namespace FlowBlox.UICore.ViewModels
{
    public class AiAssistantChatViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AiAssistantService _service;
        private readonly IDialogService _dialogService;
        private readonly IFlowBloxMessageBoxService _messageBoxService;
        private readonly IRuntimeStateService _runtimeStateService;
        private readonly SynchronizationContext? _uiContext;
        private CancellationTokenSource? _cts;
        private AssistantRunControl? _runControl;
        private string _currentInput = string.Empty;
        private bool _isBusy;
        private bool _isRuntimeActive;
        private bool _isExternalProjectEditActive;
        private Func<AIAssistantProjectStateSnapshot?>? _captureProjectState;
        private Func<AIAssistantProjectStateSnapshot, Task<bool>>? _restoreProjectState;
        private Action? _beforeAutomaticLayoutAdjustment;
        private AIAssistantProjectStateSnapshot? _stateBeforeLastPrompt;
        private AIAssistantProjectStateSnapshot? _stateAfterLastPrompt;
        private bool _isPromptStateUndone;
        private readonly AiAssistantHistoryStore _historyStore = AiAssistantHistoryStore.Instance;
        private AiAssistantHistoryDocument? _currentHistory;
        private string? _currentHistoryFilePath;
        private bool _canGoBackToHistory;
        private int _estimatedUsedTokens;
        private string _communicationStatusText = string.Empty;
        private bool _wasPaused;

        public ObservableCollection<AssistantTranscriptLine> Transcript { get; } = new ObservableCollection<AssistantTranscriptLine>();
        public RelayCommand NewHistoryCommand { get; }
        public RelayCommand BackToHistoryCommand { get; }
        public RelayCommand SubmitCommand { get; }
        public RelayCommand DemoPromptsCommand { get; }
        public RelayCommand ContinueCommand { get; }
        public RelayCommand StopCommand { get; }
        public RelayCommand PauseCommand { get; }
        public bool IsPauseRequested => _runControl?.IsPauseRequested == true;
        public bool IsPaused => _runControl?.IsPaused == true;
        public string PauseButtonText => FlowBloxResourceUtil.GetLocalizedString(
            IsPauseRequested && !IsPaused ? "Button_PausePending" : "Button_Pause", typeof(Resources.AiAssistantChatView));
        public RelayCommand CopyTranscriptEntryCommand { get; }
        public RelayCommand OpenTranscriptEntryInEditorCommand { get; }
        public RelayCommand OpenCommunicationProtocolDirectoryCommand { get; }
        public RelayCommand OpenDebugToolsCommand { get; }
        public RelayCommand UndoProjectStateCommand { get; }
        public RelayCommand RedoProjectStateCommand { get; }
        public RelayCommand ResetTokenUsageCommand { get; }

        public string CurrentInput
        {
            get => _currentInput;
            set
            {
                if (_currentInput != value)
                {
                    _currentInput = value;
                    OnPropertyChanged(nameof(CurrentInput));
                    OnPropertyChanged(nameof(IsInputEmpty));
                    SubmitCommand.Invalidate();
                }
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged(nameof(IsBusy));
                    OnPropertyChanged(nameof(CanEditInput));
                    OnPropertyChanged(nameof(InputWatermark));
                    SubmitCommand.Invalidate();
                    StopCommand.Invalidate();
                    ContinueCommand.Invalidate();
                    PauseCommand.Invalidate();
                    NewHistoryCommand.Invalidate();
                    BackToHistoryCommand.Invalidate();
                    RefreshUndoRedoState();
                }
            }
        }

        public bool CanEditInput => true;
        public bool IsInputEmpty => string.IsNullOrEmpty(CurrentInput);
        public string InputWatermark => FlowBloxResourceUtil.GetLocalizedString(
            IsBusy ? "Watermark_FollowUpPrompt" : "Watermark_InitialPrompt",
            typeof(Resources.AiAssistantChatView));
        public string ContinueButtonText => FlowBloxResourceUtil.GetLocalizedString(
            "Button_Continue", typeof(Resources.AiAssistantChatView));
        public string ContinueTooltip => FlowBloxResourceUtil.GetLocalizedString(
            "Tooltip_Continue", typeof(Resources.AiAssistantChatView));
        public string SubmitButtonText => FlowBloxResourceUtil.GetLocalizedString(
            "Button_Submit", typeof(Resources.AiAssistantChatView));
        public string CurrentConversationSummary => _service.CurrentConversationSummary;
        public bool HasConversationSummary => !string.IsNullOrWhiteSpace(CurrentConversationSummary);

        public bool CanGoBackToHistory
        {
            get => !IsBusy && _canGoBackToHistory;
            set
            {
                if (_canGoBackToHistory == value)
                    return;

                _canGoBackToHistory = value;
                OnPropertyChanged(nameof(CanGoBackToHistory));
                BackToHistoryCommand.Invalidate();
            }
        }

        public bool ShowProviderConfigurationWarning => !_isProviderConfigured;
        public bool ShowIntroHeader => Transcript.Count == 0;
        public int EstimatedUsedTokens
        {
            get => _estimatedUsedTokens;
            private set
            {
                if (_estimatedUsedTokens == value)
                    return;

                _estimatedUsedTokens = Math.Max(0, value);
                OnPropertyChanged(nameof(EstimatedUsedTokens));
                OnPropertyChanged(nameof(HasEstimatedUsedTokens));
                OnPropertyChanged(nameof(EstimatedUsedTokensText));
                ResetTokenUsageCommand?.Invalidate();
            }
        }

        public bool HasEstimatedUsedTokens => EstimatedUsedTokens > 0;
        public string CommunicationStatusText
        {
            get => _communicationStatusText;
            private set
            {
                var normalizedValue = value ?? string.Empty;
                if (string.Equals(_communicationStatusText, normalizedValue, StringComparison.Ordinal))
                    return;

                _communicationStatusText = normalizedValue;
                OnPropertyChanged(nameof(CommunicationStatusText));
                OnPropertyChanged(nameof(HasCommunicationStatus));
            }
        }

        public bool HasCommunicationStatus => !string.IsNullOrWhiteSpace(CommunicationStatusText);

        public string EstimatedUsedTokensText => string.Format(
            CultureInfo.CurrentCulture,
            FlowBloxResourceUtil.GetLocalizedString("TokenMonitor_Format", typeof(Resources.AiAssistantChatView)),
            EstimatedUsedTokens);

        public bool CanUndoProjectState =>
            !IsBusy &&
            _stateBeforeLastPrompt != null &&
            _stateAfterLastPrompt != null &&
            !_isPromptStateUndone;

        public bool CanRedoProjectState =>
            !IsBusy &&
            _stateBeforeLastPrompt != null &&
            _stateAfterLastPrompt != null &&
            _isPromptStateUndone;

        public event EventHandler? NewHistoryRequested;
        public event EventHandler? HistoryRequested;
        public event EventHandler? DebugToolsRequested;
        public event EventHandler? BeforeAutomaticLayoutAdjustment;
        private bool _isProviderConfigured;

        public AiAssistantChatViewModel()
        {
            _uiContext = SynchronizationContext.Current;
            _dialogService = FlowBloxServiceLocator.Instance.GetService<IDialogService>();
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
            _runtimeStateService = FlowBloxServiceLocator.Instance.GetService<IRuntimeStateService>();
            var toolApi = new DefaultToolApi
            {
                ToolExecutionConfirmationCallback = ConfirmToolExecutionRequest
            };
            _service = new AiAssistantService(
                new AiProviderExecutor(),
                toolApi,
                FlowBloxLogManager.Instance.GetLogger());
            _service.TranscriptLineAdded += Service_TranscriptLineAdded;
            _service.EstimatedUsedTokensChanged += Service_EstimatedUsedTokensChanged;
            _service.CommunicationStatusChanged += Service_CommunicationStatusChanged;
            _service.ConversationSummaryChanged += Service_ConversationSummaryChanged;
            _service.BeforeAutomaticLayoutAdjustment += Service_BeforeAutomaticLayoutAdjustment;

            NewHistoryCommand = new RelayCommand(() => NewHistoryRequested?.Invoke(this, EventArgs.Empty), () => !IsBusy);
            BackToHistoryCommand = new RelayCommand(RequestHistoryOverview, () => CanGoBackToHistory);
            SubmitCommand = new RelayCommand(async () => await SubmitAsync(), CanSubmit);
            DemoPromptsCommand = new RelayCommand(OpenDemoPrompts);
            ContinueCommand = new RelayCommand(Continue, CanContinue);
            StopCommand = new RelayCommand(Stop, () => IsBusy);
            PauseCommand = new RelayCommand(Pause, () => IsBusy && !IsPauseRequested);
            CopyTranscriptEntryCommand = new RelayCommand(CopyTranscriptEntry);
            OpenTranscriptEntryInEditorCommand = new RelayCommand(OpenTranscriptEntryInEditor);
            OpenCommunicationProtocolDirectoryCommand = new RelayCommand(OpenCommunicationProtocolDirectory);
            OpenDebugToolsCommand = new RelayCommand(
                () => DebugToolsRequested?.Invoke(this, EventArgs.Empty));
            UndoProjectStateCommand = new RelayCommand(async () => await UndoProjectStateAsync(), () => CanUndoProjectState);
            RedoProjectStateCommand = new RelayCommand(async () => await RedoProjectStateAsync(), () => CanRedoProjectState);
            ResetTokenUsageCommand = new RelayCommand(ResetTokenUsage, () => HasEstimatedUsedTokens);

            if (_runtimeStateService != null)
            {
                _runtimeStateService.StateChanged += RuntimeStateService_StateChanged;
                _isRuntimeActive = _runtimeStateService.IsRuntimeActive;
                _isExternalProjectEditActive = _runtimeStateService.IsExternalProjectEditActive;
            }

            RefreshProviderConfigurationState();
        }

        private void OpenDemoPrompts()
        {
            var dialog = new DemoPromptsWindow();
            if (_dialogService.ShowWPFDialog(dialog) != true ||
                string.IsNullOrWhiteSpace(dialog.SelectedPrompt))
            {
                return;
            }

            CurrentInput = dialog.SelectedPrompt;
        }

        private bool CanSubmit()
        {
            if (string.IsNullOrWhiteSpace(CurrentInput) || _isRuntimeActive)
                return false;

            return IsBusy
                ? _runControl != null
                : !_isExternalProjectEditActive;
        }

        private bool CanContinue()
        {
            return IsBusy && IsPaused;
        }

        private void Service_BeforeAutomaticLayoutAdjustment(object? sender, EventArgs e)
        {
            _beforeAutomaticLayoutAdjustment?.Invoke();
            BeforeAutomaticLayoutAdjustment?.Invoke(this, EventArgs.Empty);
        }

        private async Task SubmitAsync()
        {
            if (!CanSubmit())
                return;

            if (!IsBusy)
            {
                RefreshProviderConfigurationState();
                if (ShowProviderConfigurationWarning)
                {
                    ShowMessageBoxOnUiThread(
                        FlowBloxResourceUtil.GetLocalizedString("Warning_MissingProvider", typeof(Resources.AiAssistantChatView)),
                        FlowBloxResourceUtil.GetLocalizedString("Message_ProviderConfiguration_Title", typeof(Resources.AiAssistantChatView)),
                        FlowBloxMessageBoxTypes.Warning);
                    return;
                }
            }

            var input = CurrentInput.Trim();
            CurrentInput = string.Empty;

            EnsureCurrentHistory();
            if (IsBusy)
            {
                if (_runControl?.TryStageUserMessage(input) == true)
                {
                    AddUserTranscriptLine(input);
                    SaveCurrentHistory();
                }
                else
                {
                    CurrentInput = input;
                }
                return;
            }

            var stateBeforePrompt = _captureProjectState?.Invoke();
            var promptCompleted = false;
            AddUserTranscriptLine(input);

            var externalProjectEditActivated = false;

            try
            {
                _runControl = new AssistantRunControl();
                _runControl.StateChanged += RunControl_StateChanged;
                _wasPaused = false;
                NotifyPauseStateChanged();
                IsBusy = true;
                externalProjectEditActivated = true;
                _runtimeStateService?.SetExternalProjectEditActive(true);
                _cts = new CancellationTokenSource();

                await _service.GenerateProjectAsync(input, _cts.Token, _runControl);
                promptCompleted = true;
            }
            catch (OperationCanceledException)
            {
                AddTranscriptLine(new AssistantTranscriptLine
                {
                    Kind = AssistantTranscriptKind.Status,
                    Text = FlowBloxResourceUtil.GetLocalizedString("Status_Stopped", typeof(Resources.AiAssistantChatView)),
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                AddTranscriptLine(new AssistantTranscriptLine
                {
                    Kind = AssistantTranscriptKind.Error,
                    Text = ex.Message,
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                if (_runControl != null)
                    _runControl.StateChanged -= RunControl_StateChanged;
                _runControl = null;
                _wasPaused = false;
                NotifyPauseStateChanged();
                IsBusy = false;
                if (externalProjectEditActivated)
                    _runtimeStateService?.SetExternalProjectEditActive(false);

                _cts?.Dispose();
                _cts = null;
                ClearCommunicationStatus();

                SaveCurrentHistory();
                CapturePromptUndoRedoState(promptCompleted, stateBeforePrompt);
            }
        }

        private void CapturePromptUndoRedoState(
            bool promptCompleted,
            AIAssistantProjectStateSnapshot? stateBeforePrompt)
        {
            if (!promptCompleted || stateBeforePrompt == null)
                return;

            var stateAfterPrompt = _captureProjectState?.Invoke();
            if (stateAfterPrompt == null)
                return;

            _stateBeforeLastPrompt = stateBeforePrompt;
            _stateAfterLastPrompt = stateAfterPrompt;
            _isPromptStateUndone = false;
            RefreshUndoRedoState();
        }

        private void RuntimeStateService_StateChanged(object? sender, Events.RuntimeStateChangedEventArgs e)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                if (_isRuntimeActive == e.IsRuntimeActive &&
                    _isExternalProjectEditActive == e.IsExternalProjectEditActive)
                    return;

                _isRuntimeActive = e.IsRuntimeActive;
                _isExternalProjectEditActive = e.IsExternalProjectEditActive;
                SubmitCommand.Invalidate();
                ContinueCommand.Invalidate();
            });
        }

        private void Pause()
        {
            if (!IsBusy || _runControl == null || IsPauseRequested)
                return;

            _runControl.RequestPause();
            NotifyPauseStateChanged();
        }

        private void Continue()
        {
            if (!CanContinue())
                return;

            _runControl?.Continue();
        }

        private void RunControl_StateChanged(object? sender, EventArgs e)
        {
            SynchronizationContextHelper.PostToUi(_uiContext, () =>
            {
                var isPaused = IsPaused;
                if (isPaused && !_wasPaused)
                {
                    AddTranscriptLine(new AssistantTranscriptLine
                    {
                        Kind = AssistantTranscriptKind.Status,
                        Text = FlowBloxResourceUtil.GetLocalizedString("Status_Paused", typeof(Resources.AiAssistantChatView)),
                        Timestamp = DateTime.Now
                    });
                    SaveCurrentHistory();
                }

                _wasPaused = isPaused;
                NotifyPauseStateChanged();
            });
        }

        private void NotifyPauseStateChanged()
        {
            OnPropertyChanged(nameof(IsPauseRequested));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PauseButtonText));
            PauseCommand.Invalidate();
            ContinueCommand.Invalidate();
            StopCommand.Invalidate();
        }

        private void Stop()
        {
            if (!IsBusy)
                return;

            _cts?.Cancel();
        }

        private void AddUserTranscriptLine(string input)
        {
            AddTranscriptLine(new AssistantTranscriptLine
            {
                Kind = AssistantTranscriptKind.User,
                Text = input,
                Timestamp = DateTime.Now
            });
        }

        private void RequestHistoryOverview()
        {
            if (HasUnsavedFirstMessageDraft() && !ConfirmDiscardFirstMessageDraft())
                return;

            HistoryRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool HasUnsavedFirstMessageDraft()
        {
            return Transcript.Count == 0 && !string.IsNullOrWhiteSpace(CurrentInput);
        }

        private bool ConfirmDiscardFirstMessageDraft()
        {
            var decision = _messageBoxService?.ShowMessageBox(
                FlowBloxResourceUtil.GetLocalizedString("Message_DiscardUnsentDraft_Description", typeof(Resources.AiAssistantChatView)),
                FlowBloxResourceUtil.GetLocalizedString("Message_DiscardUnsentDraft_Title", typeof(Resources.AiAssistantChatView)),
                FlowBloxMessageBoxTypes.Question);

            return decision == FlowBloxMessageBoxDialogResult.Yes;
        }

        private async Task UndoProjectStateAsync()
        {
            if (!CanUndoProjectState || _restoreProjectState == null || _stateBeforeLastPrompt == null)
                return;

            if (await _restoreProjectState.Invoke(_stateBeforeLastPrompt))
            {
                _isPromptStateUndone = true;
                RefreshUndoRedoState();
            }
        }

        private async Task RedoProjectStateAsync()
        {
            if (!CanRedoProjectState || _restoreProjectState == null || _stateAfterLastPrompt == null)
                return;

            if (await _restoreProjectState.Invoke(_stateAfterLastPrompt))
            {
                _isPromptStateUndone = false;
                RefreshUndoRedoState();
            }
        }

        private void Service_TranscriptLineAdded(object? sender, AssistantTranscriptLine line)
        {
            AddTranscriptLine(line);
        }

        private void Service_EstimatedUsedTokensChanged(object? sender, int estimatedUsedTokens)
        {
            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                _uiContext.Post(_ => EstimatedUsedTokens = estimatedUsedTokens, null);
                return;
            }

            EstimatedUsedTokens = estimatedUsedTokens;
        }

        private void Service_CommunicationStatusChanged(object? sender, AssistantCommunicationStatusChangedEventArgs e)
        {
            var text = e?.IsVisible == true ? e.Text : string.Empty;
            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                _uiContext.Post(_ => CommunicationStatusText = text, null);
                return;
            }

            CommunicationStatusText = text;
        }

        public AiAssistantDebugViewModel CreateDebugViewModel() => new(_service, this);

        private void Service_ConversationSummaryChanged(object? sender, EventArgs e)
        {
            void NotifySummaryChanged()
            {
                OnPropertyChanged(nameof(CurrentConversationSummary));
                OnPropertyChanged(nameof(HasConversationSummary));

            }

            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                _uiContext.Post(_ => NotifySummaryChanged(), null);
                return;
            }

            NotifySummaryChanged();
        }

        private void ResetTokenUsage()
        {
            _service.ResetEstimatedUsedTokens();
            SaveCurrentHistory();
        }

        private bool ConfirmToolExecutionRequest(ToolRequest request)
        {
            if (request == null)
                return true;

            if (string.Equals(request.ToolName, "ExecuteInputFileCommand", StringComparison.OrdinalIgnoreCase) &&
                !ConfirmInputFileCommandExecution(request))
                return false;

            if (string.Equals(request.ToolName, "RunProjectDebugTest", StringComparison.OrdinalIgnoreCase) &&
                !ConfirmExternalProjectDebugExecution(request))
                return false;

            if (string.Equals(request.ToolName, "ExecuteExternalProject", StringComparison.OrdinalIgnoreCase) &&
                !ConfirmProjectExecution(request))
                return false;

            return true;
        }

        private bool ConfirmExternalProjectDebugExecution(ToolRequest request)
        {
            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(
                request.Arguments?.Value<string>("projectFile") ??
                request.Arguments?.Value<string>("confirmationProjectFile") ??
                string.Empty);

            var reason = (request.Arguments?.Value<string>("reason") ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(reason) ||
                   ShowExternalProjectExecutionConfirmation(projectFile, reason, request);
        }

        private bool ConfirmProjectExecution(ToolRequest request)
        {
            var reason = (request.Arguments?.Value<string>("reason") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(reason))
                return true;

            var projectFile = FlowBloxFieldHelper.ReplaceFieldsInString(
                request.Arguments?.Value<string>("projectFile") ?? string.Empty);
            return ShowExternalProjectExecutionConfirmation(projectFile, reason, request);
        }

        private bool ConfirmInputFileCommandExecution(ToolRequest request)
        {
            var key = request.Arguments?.Value<string>("key") ?? string.Empty;
            var message = string.Format(
                FlowBloxResourceUtil.GetLocalizedString("Message_ExecuteInputFileCommand_Confirm_Description", typeof(Resources.AiAssistantChatView)),
                key);

            var title = FlowBloxResourceUtil.GetLocalizedString("Message_ExecuteInputFileCommand_Confirm_Title", typeof(Resources.AiAssistantChatView));
            var decision = ShowMessageBoxOnUiThread(message, title, FlowBloxMessageBoxTypes.Question);

            return decision == FlowBloxMessageBoxDialogResult.Yes;
        }

        private bool ShowExternalProjectExecutionConfirmation(
            string projectFile,
            string reason,
            ToolRequest request)
        {
            bool? Show()
            {
                var window = new ExternalProjectExecutionConfirmationWindow(projectFile, reason, request.ConfirmationInputParameters);
                var result = _dialogService.ShowWPFDialog(window);
                request.RememberProjectApproval = result == true && window.RememberProjectApproval;
                return result;
            }

            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                bool? result = false;
                Exception? exception = null;
                _uiContext.Send(_ =>
                {
                    try { result = Show(); }
                    catch (Exception ex) { exception = ex; }
                }, null);
                if (exception != null)
                    throw exception;
                return result == true;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(Show) == true;

            return Show() == true;
        }

        private FlowBloxMessageBoxDialogResult? ShowMessageBoxOnUiThread(
            string message,
            string title,
            FlowBloxMessageBoxTypes messageBoxType)
        {
            if (_messageBoxService == null)
                return null;

            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                FlowBloxMessageBoxDialogResult? result = null;
                Exception? exception = null;

                _uiContext.Send(_ =>
                {
                    try
                    {
                        result = _messageBoxService.ShowMessageBox(message, title, messageBoxType);
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }, null);

                if (exception != null)
                    throw exception;

                return result;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => _messageBoxService.ShowMessageBox(message, title, messageBoxType));

            return _messageBoxService.ShowMessageBox(message, title, messageBoxType);
        }

        private void CopyTranscriptEntry(object parameter)
        {
            if (parameter is not AssistantTranscriptLine line)
                return;

            var content = GetTranscriptContent(line);
            if (string.IsNullOrWhiteSpace(content))
                return;

            Clipboard.SetText(content);
        }

        private void OpenTranscriptEntryInEditor(object parameter)
        {
            if (parameter is not AssistantTranscriptLine line)
                return;

            var content = GetTranscriptContent(line);
            if (string.IsNullOrWhiteSpace(content))
                return;

            var subject = $"AIAssistant_{line.Timestamp:yyyyMMdd_HHmmss}_{line.Kind}";
            FlowBloxEditingHelper.OpenUsingEditor(content, subject);
        }

        private void OpenCommunicationProtocolDirectory(object _)
        {
            try
            {
                var options = FlowBloxOptions.GetOptionInstance();
                var directory = options.GetOption(OptionsConstants.AICommuncationProtocolDir)?.Value;
                if (string.IsNullOrWhiteSpace(directory))
                {
                    directory = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "FlowBlox",
                        "logs",
                        "ai_assistant_protocol");
                }

                if (!Directory.Exists(directory))
                {
                    _messageBoxService?.ShowMessageBox(
                        string.Format(
                            FlowBloxResourceUtil.GetLocalizedString("Message_CommunicationProtocolDirectory_NotFound_Description", typeof(Resources.AiAssistantChatView)),
                            directory),
                        FlowBloxResourceUtil.GetLocalizedString("Message_CommunicationProtocolDirectory_NotFound_Title", typeof(Resources.AiAssistantChatView)),
                        FlowBloxMessageBoxTypes.Warning);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = directory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _messageBoxService?.ShowMessageBox(
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("Message_CommunicationProtocolDirectory_OpenFailed_Description", typeof(Resources.AiAssistantChatView)),
                        ex.Message),
                    FlowBloxResourceUtil.GetLocalizedString("Message_CommunicationProtocolDirectory_OpenFailed_Title", typeof(Resources.AiAssistantChatView)),
                    FlowBloxMessageBoxTypes.Error);
            }
        }

        public void StartNewHistory()
        {
            if (IsBusy)
                return;

            var snapshot = _captureProjectState?.Invoke();
            _service.ResetSession();
            Transcript.Clear();
            NotifyTranscriptStateChanged();
            ClearCommunicationStatus();
            _currentHistory = new AiAssistantHistoryDocument
            {
                HistoryGuid = Guid.NewGuid(),
                ProjectGuid = snapshot?.ProjectGuid ?? Guid.Empty,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now
            };
            _currentHistoryFilePath = null;
            CurrentInput = string.Empty;
            RefreshNavigationState();
        }

        public void OpenHistory(AiAssistantHistoryListItem item)
        {
            if (IsBusy || item == null)
                return;

            var history = _historyStore.Load(item.FilePath);
            if (history == null)
                return;

            _currentHistory = history;
            _currentHistoryFilePath = item.FilePath;
            Transcript.Clear();
            foreach (var line in history.Transcripts ?? new List<AssistantTranscriptLine>())
                Transcript.Add(line);

            _service.RestoreSession(history);
            EstimatedUsedTokens = _service.EstimatedUsedTokens;
            ClearCommunicationStatus();
            NotifyTranscriptStateChanged();
            CurrentInput = string.Empty;
        }

        private void EnsureCurrentHistory()
        {
            if (_currentHistory != null)
                return;

            var snapshot = _captureProjectState?.Invoke();
            _currentHistory = new AiAssistantHistoryDocument
            {
                HistoryGuid = Guid.NewGuid(),
                ProjectGuid = snapshot?.ProjectGuid ?? Guid.Empty,
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        private void SaveCurrentHistory()
        {
            if (_currentHistory == null || Transcript.Count == 0)
                return;

            _service.UpdateHistorySessionMetadata(_currentHistory);
            _currentHistory.Transcripts = Transcript.ToList();
            _currentHistoryFilePath = _historyStore.CreateOrUpdateHistory(_currentHistory, _currentHistoryFilePath);
            RefreshNavigationState();
        }

        private void RefreshHistories()
        {
            var projectGuid = _captureProjectState?.Invoke()?.ProjectGuid;
            _historyStore.Initialize(projectGuid);
            _historyStore.Refresh(projectGuid);
            RefreshNavigationState();
        }

        private void RefreshNavigationState()
        {
            OnPropertyChanged(nameof(CanGoBackToHistory));
            BackToHistoryCommand.Invalidate();
            NewHistoryCommand.Invalidate();
        }
        private static string GetTranscriptContent(AssistantTranscriptLine line)
        {
            if (line == null)
                return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {line.Timestamp:O}");
            sb.AppendLine($"Kind: {line.Kind}");

            if (!string.IsNullOrWhiteSpace(line.Text))
            {
                sb.AppendLine();
                sb.AppendLine("Text:");
                sb.AppendLine(line.Text);
            }

            if (!string.IsNullOrWhiteSpace(line.InternalContent))
            {
                sb.AppendLine();
                sb.AppendLine("Details:");
                sb.AppendLine(line.InternalContent);
            }

            return sb.ToString().TrimEnd();
        }

        private void AddTranscriptLine(AssistantTranscriptLine line)
        {
            if (line == null)
                return;

            if (_uiContext != null && _uiContext != SynchronizationContext.Current)
            {
                _uiContext.Post(_ =>
                {
                    Transcript.Add(line);
                    NotifyTranscriptStateChanged();
                }, null);
                return;
            }

            Transcript.Add(line);
            NotifyTranscriptStateChanged();
        }

        public void ResetForProjectInitialization()
        {
            Stop();
            _service.ResetSession();
            Transcript.Clear();
            NotifyTranscriptStateChanged();
            ClearCommunicationStatus();
            _currentHistory = null;
            _currentHistoryFilePath = null;
            RefreshHistories();
            CurrentInput = string.Empty;
            IsBusy = false;
            _runtimeStateService?.SetExternalProjectEditActive(false);
            _stateBeforeLastPrompt = null;
            _stateAfterLastPrompt = null;
            _isPromptStateUndone = false;
            RefreshProviderConfigurationState();
            RefreshHistories();
            RefreshUndoRedoState();
        }

        private void NotifyTranscriptStateChanged()
        {
            OnPropertyChanged(nameof(ShowIntroHeader));
        }

        private void ClearCommunicationStatus()
        {
            CommunicationStatusText = string.Empty;
        }

        public void ConfigureProjectStateAccess(
            Func<AIAssistantProjectStateSnapshot?> captureProjectState,
            Func<AIAssistantProjectStateSnapshot, Task<bool>> restoreProjectState)
        {
            _captureProjectState = captureProjectState;
            _restoreProjectState = restoreProjectState;
            RefreshHistories();
            var snapshot = _captureProjectState?.Invoke();
            if (_currentHistory != null && _currentHistory.ProjectGuid == Guid.Empty && Transcript.Count == 0)
                _currentHistory.ProjectGuid = snapshot?.ProjectGuid ?? Guid.Empty;

            RefreshUndoRedoState();
        }

        public void ConfigureBeforeAutomaticLayoutAdjustment(Action beforeAutomaticLayoutAdjustment)
        {
            _beforeAutomaticLayoutAdjustment = beforeAutomaticLayoutAdjustment;
        }

        public AssistantConfiguration GetConfiguration(out string error) => _service.GetConfiguration(out error);

        public bool SaveConfiguration(AssistantConfiguration configuration, out string error) =>
            SaveConfigurationInternal(configuration, out error);

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void RefreshUndoRedoState()
        {
            OnPropertyChanged(nameof(CanUndoProjectState));
            OnPropertyChanged(nameof(CanRedoProjectState));
            UndoProjectStateCommand.Invalidate();
            RedoProjectStateCommand.Invalidate();
        }

        public void RefreshProviderConfigurationState()
        {
            var configuration = _service.GetConfiguration(out _);
            var provider = configuration?.Provider;
            var isConfigured = provider != null &&
                !string.IsNullOrWhiteSpace(FlowBloxFieldHelper.ReplaceFieldsInString(provider.ApiKey));

            if (_isProviderConfigured == isConfigured)
                return;

            _isProviderConfigured = isConfigured;
            OnPropertyChanged(nameof(ShowProviderConfigurationWarning));
        }

        private bool SaveConfigurationInternal(AssistantConfiguration configuration, out string error)
        {
            var success = _service.SaveConfiguration(configuration, out error);
            if (success)
                RefreshProviderConfigurationState();
            RefreshHistories();

            return success;
        }

        public void Dispose()
        {
            if (_runtimeStateService != null)
                _runtimeStateService.StateChanged -= RuntimeStateService_StateChanged;

            _service.TranscriptLineAdded -= Service_TranscriptLineAdded;
            _service.EstimatedUsedTokensChanged -= Service_EstimatedUsedTokensChanged;
            _service.CommunicationStatusChanged -= Service_CommunicationStatusChanged;
            _service.ConversationSummaryChanged -= Service_ConversationSummaryChanged;
            _service.BeforeAutomaticLayoutAdjustment -= Service_BeforeAutomaticLayoutAdjustment;
        }
    }
}
