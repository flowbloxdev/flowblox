using FlowBlox.Core.Extensions;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Validations;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Manager;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FlowBlox.UICore.ViewModels.PropertyView
{
    public class PropertyViewModel : INotifyPropertyChanged
    {
        private FlowBloxTransactionEventHandler _transactionEventHandler;
        private bool _deepCopy;
        private bool _detached;
        private bool _readOnly;
        private bool _isDirty;
        private object _target;
        private object _parent;
        private string _preselectedProperty;
        private object _preselectedInstance;
        private object _transientTarget;
        private TabViewModel _selectedTab;
        private bool _isRestoringTabSelection;
        private readonly Window _window;

        public ObservableCollection<TabViewModel> Tabs { get; }

        public ICollectionView VisibleTabs { get; }

        public PropertyWindowCommitStatus CommitStatus =>
            _transactionEventHandler?.CommitStatus ?? PropertyWindowCommitStatus.None;

        public object WorkingCopy => _transientTarget;

        public object AppendWorkingCopy(object value) => _transactionEventHandler.Append(value);

        public event EventHandler WorkingCopyChanged;

        public async Task RefreshAfterAssistantAsync()
        {
            await ResolveTabsAndRestoreSelectedTab(GetSelectedTabGroupKey());
            IsDirty = true;
        }

        public bool HasActiveTransaction => _transactionEventHandler?.HasActiveTransaction == true;

        public TabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (ReferenceEquals(_selectedTab, value))
                    return;

                var tabWasSelected = _selectedTab != null;
                _selectedTab = value;
                OnPropertyChanged();

                if (tabWasSelected && !_isRestoringTabSelection)
                    CloseNestedTransactions();
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (_isDirty != value)
                {
                    _isDirty = value;
                    OnPropertyChanged(nameof(IsDirty));
                }
            }
        }

        public PropertyViewModel() 
        {
            Tabs = new ObservableCollection<TabViewModel>();
            VisibleTabs = CollectionViewSource.GetDefaultView(Tabs);
            VisibleTabs.Filter = tab => ((TabViewModel)tab).Controls?.Any(x => x.IsActive) == true;
        }

        public PropertyViewModel(Window window) : this()
        {
            _window = window;
        }

        public void Open(
            object target,
            object parent,
            bool deepCopy,
            bool readOnly,
            string preselectedProperty = "",
            object preselectedInstance = null,
            bool detached = false,
            bool nestedTransaction = false)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            _deepCopy = deepCopy;
            _detached = detached;
            _readOnly = readOnly;
            _target = target;
            _parent = parent;
            _preselectedProperty = preselectedProperty;
            _preselectedInstance = preselectedInstance;

            if (_transactionEventHandler?.HasActiveTransaction == true)
                throw new InvalidOperationException("The current property view transaction must be completed before another target can be opened.");

            _transactionEventHandler = new FlowBloxTransactionEventHandler(
                _target,
                _deepCopy,
                _detached,
                _readOnly,
                nestedTransaction);
            _transientTarget = _transactionEventHandler.Open();

            _ = ResolveTabsAndPreselectProperty();
        }

        private async Task ResolveTabsAndPreselectProperty()
        {
            await ResolveTabs();
            PreselectProperty();
        }

        private async Task ResolveTabsAndRestoreSelectedTab(string groupKey)
        {
            await ResolveTabs();
            RestoreSelectedTab(groupKey);
        }

        private void PreselectProperty()
        {
            if (!string.IsNullOrEmpty(_preselectedProperty))
                SelectProperty(_preselectedProperty);
        }

        private string GetSelectedTabGroupKey()
            => SelectedTab?.GroupKey;

        private void RestoreSelectedTab(string groupKey)
        {
            if (string.IsNullOrWhiteSpace(groupKey))
                return;

            var tab = Tabs.FirstOrDefault(x =>
                string.Equals(x.GroupKey, groupKey, StringComparison.Ordinal) &&
                x.Controls.Any(control => control.IsActive));

            if (tab == null)
                return;

            _isRestoringTabSelection = true;
            try
            {
                SelectedTab = tab;
            }
            finally
            {
                _isRestoringTabSelection = false;
            }
        }

        private void SelectProperty(string propertyName)
        {
            foreach (var tab in Tabs)
            {
                foreach (var control in tab.Controls)
                {
                    if (control.PropertyName == propertyName)
                    {
                        control.Control.Loaded += (s, e) =>
                        {
                            control.Control.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                            var textBox = VisualTreeHelperExtensions.FindFirstChild<TextBox>(control.Control);
                            if (textBox != null)
                            {
                                textBox.Focus();
                                textBox.SelectAll();
                            }
                        };

                        _window.Dispatcher.InvokeAsync(() =>
                        {
                            var tabControl = VisualTreeHelperExtensions.FindChild<TabControl>(_window);
                            if (tabControl != null)
                                tabControl.SelectedItem = tab;
                        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                        return;
                    }
                }
            }
        }

        private async Task ResolveTabs()
        {
            _selectedTab = null;
            OnPropertyChanged(nameof(SelectedTab));
            Tabs.Clear();

            try
            {
                var resolver = new PropertyViewSchemaResolver(_window, _parent);
                var tabs = resolver.ResolveTabs(_transientTarget, _readOnly, _preselectedInstance).ToList();
                Tabs.AddRange(tabs);
                VisibleTabs.Refresh();
                SubscribeToPropertyViewModelEvents(tabs);
            }
            catch (Exception ex)
            {
                FlowBloxLogManager.Instance.GetLogger().Error("Failed to resolve property view tabs.", ex);

                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("ResolveTabsFailed_Title", typeof(Resources.PropertyView)),
                    FlowBloxResourceUtil.GetLocalizedString("ResolveTabsFailed_Message", typeof(Resources.PropertyView))
                );
            }
        }

        private void SubscribeToPropertyViewModelEvents(IEnumerable<TabViewModel> tabViewModels)
        {
            foreach (var propertyControlViewModel in tabViewModels.SelectMany(x => x.Controls))
            {
                SubscribeToPropertyViewModelEvents(propertyControlViewModel);
            }
        }

        private void SubscribeToPropertyViewModelEvents(PropertyControlViewModel propertyControlViewModel)
        {
            propertyControlViewModel.AllowHasChangesUpdate = true;
            propertyControlViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PropertyControlViewModel.HasChanges) && propertyControlViewModel.HasChanges)
                    this.IsDirty = propertyControlViewModel.HasChanges;
            };
            Trace.TraceInformation(propertyControlViewModel.PropertyName);
            propertyControlViewModel.AssociationBeforeLink += PropertyControlViewModel_AssociationBeforeLink;
        }

        private void PropertyControlViewModel_AssociationBeforeLink(object? sender, Events.AssociationBeforeLinkEventArgs e)
        {
            if (!_deepCopy)
                return;

            if (_transientTarget is not BaseFlowBlock flowBlock)
                return;

            if (!IsBackReferencedProperty(flowBlock, e.PropertyName))
                return;

            e.LinkedObject = _transactionEventHandler.Append(e.OriginalLinkedObject);
        }

        private static bool IsBackReferencedProperty(BaseFlowBlock flowBlock, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(propertyName))
                return false;

            return flowBlock
                .GetBackReferencedPropertyNames()
                .Contains(propertyName);
        }

        public async Task<bool> SaveAsync(MetroWindow window, bool withoutVerification = false)
        {
            if (!await PrepareCommitAsync(window, withoutVerification))
                return false;

            return _transactionEventHandler.Save();
        }

        public async Task<bool> ApplyAsync(MetroWindow window, bool withoutVerification = false)
        {
            var selectedTabGroupKey = GetSelectedTabGroupKey();
            if (!await PrepareCommitAsync(window, withoutVerification))
                return false;

            if (!_transactionEventHandler.Apply())
                return false;

            _transientTarget = _transactionEventHandler.WorkingCopy;
            WorkingCopyChanged?.Invoke(this, EventArgs.Empty);
            await ResolveTabsAndRestoreSelectedTab(selectedTabGroupKey);
            IsDirty = false;
            return true;
        }

        private async Task<bool> PrepareCommitAsync(MetroWindow window, bool withoutVerification = false)
        {
            if (_readOnly)
                return false;

            if (!await PrepareNestedTransactionsAsync(window, withoutVerification))
                return false;

            if (!withoutVerification && 
                !ValidateTarget(_transientTarget, out var validationMessages))
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    window,
                    FlowBloxResourceUtil.GetLocalizedString("Global_ValidationFailed_Title"),
                    string.Join("\n", validationMessages)
                );
                OnValidationFailed();
                return false;
            }

            if (_transientTarget is FlowBloxComponent transientFlowBloxComponent &&
                transientFlowBloxComponent.HasUnusedFieldElements(out var unusedFieldElements))
            {
                var unusedFieldsWindow = new UnusedRequiredFieldsWindow(unusedFieldElements)
                {
                    Owner = window
                };

                if (unusedFieldsWindow.ShowDialog() != true)
                    return false;

                var selectedFieldElements = unusedFieldsWindow.GetSelectedFieldElements();
                transientFlowBloxComponent.RemoveRequiredFields(selectedFieldElements);
            }

            return true;
        }

        public void Cancel(bool keepComponent = false)
        {
            CloseNestedTransactions();
            _transactionEventHandler?.Rollback();
            _transactionEventHandler = null;

            if (keepComponent)
            {
                Tabs.Clear();

                _readOnly = false;
                _deepCopy = false;
                _detached = false;
                _target = null;
            }
        }

        public void HandleClosing(object sender, CancelEventArgs e)
        {
            CloseNestedTransactions();
            _transactionEventHandler?.HandleClosing(sender, e);
        }

        public void CloseEmbeddedTransaction()
        {
            CloseNestedTransactions();
            _transactionEventHandler?.CloseEmbeddedTransaction();
        }

        private IEnumerable<IPropertyViewNestedTransaction> GetNestedTransactions() =>
            Tabs.SelectMany(x => x.Controls)
                .Select(x => x.NestedTransaction)
                .Where(x => x != null);

        private async Task<bool> PrepareNestedTransactionsAsync(MetroWindow window, bool withoutVerification)
        {
            foreach (var nestedTransaction in GetNestedTransactions().Reverse())
            {
                if (!await nestedTransaction.PrepareHostCommitAsync(window, withoutVerification))
                    return false;
            }

            return true;
        }

        private void CloseNestedTransactions()
        {
            foreach (var nestedTransaction in GetNestedTransactions().Reverse())
                nestedTransaction.Cancel();
        }

        private bool ValidateTarget(object target, out List<string> messages)
        {
            messages = target is FlowBloxReactiveObject reactiveTarget
                ? FlowBloxComponentValidator.Validate(reactiveTarget)
                : new List<string>();
            return messages.Count == 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler ValidationFailed;

        protected virtual void OnValidationFailed()
        {
            ValidationFailed?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
