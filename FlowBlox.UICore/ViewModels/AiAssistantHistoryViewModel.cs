using FlowBlox.AIAssistant.History;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace FlowBlox.UICore.ViewModels
{
    public sealed class AiAssistantHistoryViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly AiAssistantHistoryStore _historyStore = AiAssistantHistoryStore.Instance;
        private readonly IFlowBloxMessageBoxService? _messageBoxService;
        private Func<AIAssistantProjectStateSnapshot?>? _captureProjectState;

        public ObservableCollection<AiAssistantHistoryListItem> Histories { get; } = new();
        public RelayCommand NewHistoryCommand { get; }
        public RelayCommand OpenHistoryCommand { get; }
        public RelayCommand DeleteHistoryCommand { get; }

        public bool HasHistories => Histories.Count > 0;

        public string EmptyMessage => FlowBloxResourceUtil.GetLocalizedString(
            "Empty_Description", typeof(Resources.AiAssistantHistoryView));

        public event EventHandler? NewHistoryRequested;
        public event EventHandler<AiAssistantHistoryListItem>? HistoryOpenRequested;
        public event PropertyChangedEventHandler? PropertyChanged;

        public AiAssistantHistoryViewModel()
        {
            _messageBoxService = FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>();
            NewHistoryCommand = new RelayCommand(() => NewHistoryRequested?.Invoke(this, EventArgs.Empty));
            OpenHistoryCommand = new RelayCommand(OpenHistory, parameter => parameter is AiAssistantHistoryListItem);
            DeleteHistoryCommand = new RelayCommand(DeleteHistory, parameter => parameter is AiAssistantHistoryListItem);
            Histories.CollectionChanged += Histories_CollectionChanged;
            _historyStore.Histories.CollectionChanged += StoreHistories_CollectionChanged;
        }

        public void ConfigureProjectStateAccess(Func<AIAssistantProjectStateSnapshot?> captureProjectState)
        {
            _captureProjectState = captureProjectState;
            Refresh();
        }

        public void Refresh()
        {
            var snapshot = _captureProjectState?.Invoke();
            var scope = snapshot?.ComponentScope ?? string.Empty;
            var items = _historyStore.LoadList(snapshot?.ProjectGuid)
                .Where(item => string.Equals(item.ComponentScope, scope, StringComparison.Ordinal))
                .ToList();
            Histories.Clear();
            foreach (var item in items)
                Histories.Add(item);
            OnPropertyChanged(nameof(HasHistories));
        }

        private void OpenHistory(object parameter)
        {
            if (parameter is AiAssistantHistoryListItem item)
                HistoryOpenRequested?.Invoke(this, item);
        }

        private void DeleteHistory(object parameter)
        {
            if (parameter is not AiAssistantHistoryListItem item || _messageBoxService == null)
                return;

            var confirmResult = _messageBoxService.ShowMessageBox(
                Resources.AiAssistantHistoryView.Message_DeleteHistory_Confirm_Description,
                Resources.AiAssistantHistoryView.Message_DeleteHistory_Confirm_Title,
                FlowBloxMessageBoxTypes.Question);

            if (confirmResult != FlowBloxMessageBoxDialogResult.Yes)
                return;

            try
            {
                _historyStore.Delete(item);
            }
            catch (Exception ex)
            {
                _messageBoxService.ShowMessageBox(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resources.AiAssistantHistoryView.Message_DeleteHistory_Failed_Description,
                        ex.Message),
                    Resources.AiAssistantHistoryView.Message_DeleteHistory_Failed_Title,
                    FlowBloxMessageBoxTypes.Error);
            }
        }

        private void Histories_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasHistories));
        }

        private void StoreHistories_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

        public void Dispose()
        {
            _historyStore.Histories.CollectionChanged -= StoreHistories_CollectionChanged;
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
