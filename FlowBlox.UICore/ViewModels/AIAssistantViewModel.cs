using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.History;
using FlowBlox.Core.Models.Components;
using FlowBlox.UICore.Commands;
using System.Collections.Specialized;
using System.ComponentModel;

namespace FlowBlox.UICore.ViewModels
{
    public sealed class AIAssistantProjectStateSnapshot
    {
        public Guid ProjectGuid { get; init; }
        public string ComponentScope { get; init; } = string.Empty;
        public string ProjectName { get; init; }
        public string ProjectFilePath { get; init; }
        public string ProjectJson { get; init; }
        public string ExtensionsJson { get; init; }
        public string ProjectSpaceGuid { get; init; }
        public int? ProjectSpaceVersion { get; init; }
        public string ProjectSpaceEndpointUri { get; init; }
    }

    public sealed class AIAssistantViewModel : INotifyPropertyChanged, IDisposable
    {
        private bool _isHistoryOverviewVisible = true;

        public AiAssistantChatViewModel ChatViewModel { get; } = new();
        public AiAssistantHistoryViewModel HistoryViewModel { get; } = new();
        public RelayCommand NewChatCommand { get; }
        public RelayCommand BackToHistoryCommand { get; }
        public RelayCommand UndoProjectStateCommand { get; }
        public RelayCommand RedoProjectStateCommand { get; }
        public RelayCommand OpenCommunicationProtocolDirectoryCommand { get; }

        public bool IsHistoryOverviewVisible
        {
            get => _isHistoryOverviewVisible;
            private set
            {
                if (_isHistoryOverviewVisible == value)
                    return;

                _isHistoryOverviewVisible = value;
                OnPropertyChanged(nameof(IsHistoryOverviewVisible));
                OnPropertyChanged(nameof(IsChatVisible));
                InvalidateToolbarCommands();
            }
        }

        public bool IsComponentMode => ChatViewModel.IsComponentMode;

        public bool IsChatVisible => !IsHistoryOverviewVisible;
        public string CurrentConversationSummary => ChatViewModel.CurrentConversationSummary;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AIAssistantViewModel()
        {
            NewChatCommand = new RelayCommand(StartNewChat, () => !ChatViewModel.IsBusy);
            BackToHistoryCommand = new RelayCommand(
                () => ChatViewModel.BackToHistoryCommand.Execute(null),
                () => IsChatVisible && ChatViewModel.BackToHistoryCommand.CanExecute(null));
            UndoProjectStateCommand = new RelayCommand(
                () => ChatViewModel.UndoProjectStateCommand.Execute(null),
                () => IsChatVisible && ChatViewModel.UndoProjectStateCommand.CanExecute(null));
            RedoProjectStateCommand = new RelayCommand(
                () => ChatViewModel.RedoProjectStateCommand.Execute(null),
                () => IsChatVisible && ChatViewModel.RedoProjectStateCommand.CanExecute(null));
            OpenCommunicationProtocolDirectoryCommand = new RelayCommand(
                () => ChatViewModel.OpenCommunicationProtocolDirectoryCommand.Execute(null),
                () => ChatViewModel.OpenCommunicationProtocolDirectoryCommand.CanExecute(null));

            ChatViewModel.NewHistoryRequested += (_, _) => StartNewChat();
            ChatViewModel.HistoryRequested += (_, _) => ShowHistoryOverview();
            ChatViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AiAssistantChatViewModel.IsComponentMode))
                    OnPropertyChanged(nameof(IsComponentMode));
                InvalidateToolbarCommands();
            };

            HistoryViewModel.NewHistoryRequested += (_, _) => StartNewChat();
            HistoryViewModel.HistoryOpenRequested += (_, item) => OpenHistory(item);
            HistoryViewModel.Histories.CollectionChanged += Histories_CollectionChanged;

            RefreshChatNavigationState();
        }

        public void ResetForProjectInitialization()
        {
            ChatViewModel.ResetForProjectInitialization();
            ShowHistoryOverview();
        }

        public void ConfigureProjectStateAccess(
            Func<AIAssistantProjectStateSnapshot?> captureProjectState,
            Func<AIAssistantProjectStateSnapshot, Task<bool>> restoreProjectState)
        {
            ChatViewModel.ConfigureProjectStateAccess(captureProjectState, restoreProjectState);
            HistoryViewModel.ConfigureProjectStateAccess(captureProjectState);
            IsHistoryOverviewVisible = true;
            RefreshChatNavigationState();
        }

        public void ConfigureBeforeAutomaticLayoutAdjustment(Action beforeAutomaticLayoutAdjustment)
        {
            ChatViewModel.ConfigureBeforeAutomaticLayoutAdjustment(beforeAutomaticLayoutAdjustment);
        }

        public AssistantConfiguration GetConfiguration(out string error) =>
            ChatViewModel.GetConfiguration(out error);

        public bool SaveConfiguration(AssistantConfiguration configuration, out string error)
        {
            var success = ChatViewModel.SaveConfiguration(configuration, out error);
            HistoryViewModel.Refresh();
            RefreshChatNavigationState();
            return success;
        }

        private void StartNewChat()
        {
            ChatViewModel.StartNewHistory();
            IsHistoryOverviewVisible = false;
            RefreshChatNavigationState();
        }

        private void OpenHistory(AiAssistantHistoryListItem item)
        {
            ChatViewModel.OpenHistory(item);
            IsHistoryOverviewVisible = false;
            RefreshChatNavigationState();
        }

        private void ShowHistoryOverview()
        {
            HistoryViewModel.Refresh();
            IsHistoryOverviewVisible = true;
            RefreshChatNavigationState();
        }

        private void Histories_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RefreshChatNavigationState();
        }

        private void RefreshChatNavigationState()
        {
            ChatViewModel.CanGoBackToHistory = true;
            InvalidateToolbarCommands();
        }

        private void InvalidateToolbarCommands()
        {
            NewChatCommand?.Invalidate();
            BackToHistoryCommand?.Invalidate();
            UndoProjectStateCommand?.Invalidate();
            RedoProjectStateCommand?.Invalidate();
            OpenCommunicationProtocolDirectoryCommand?.Invalidate();
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            ChatViewModel.Dispose();
            HistoryViewModel.Dispose();
        }
    }
}
