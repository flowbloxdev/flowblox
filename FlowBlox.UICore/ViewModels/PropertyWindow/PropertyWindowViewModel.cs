using FlowBlox.Core.Constants;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Provider;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.PropertyWindow;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FlowBlox.UICore.ViewModels.PropertyView
{
    public class PropertyWindowViewModel : INotifyPropertyChanged
    {
        private const string ApplyOptionValue = "Apply";
        private const string ApplyWithoutVerificationOptionValue = "ApplyWithoutVerification";
        private const string SaveOptionValue = "Save";
        private const string SaveWithoutVerificationOptionValue = "SaveWithoutVerification";
        private static readonly TimeSpan UIActionsRefreshDebounceInterval = TimeSpan.FromSeconds(1);
        private readonly MetroWindow _window;
        private object _uiActionTarget;
        private bool _isClosed;
        private DispatcherTimer _uiActionsRefreshDebounceTimer;

        public bool DisplaySaveButton { get; }

        public bool EnableApplySplitButton { get; }

        public bool EnableSaveSplitButton { get; }

        public PropertyWindowCommitStatus CommitStatus =>
            PropertyViewModel?.CommitStatus ?? PropertyWindowCommitStatus.None;

        public RelayCommand SaveCommand { get; }

        public RelayCommand ApplyCommand { get; }

        public RelayCommand ApplyWithoutVerificationCommand { get; }

        public RelayCommand SaveWithoutVerificationCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand RefreshUIActionsCommand { get; }

        public IReadOnlyList<PropertyWindowCommitActionViewModel> ApplyButtonActions { get; }

        public IReadOnlyList<PropertyWindowCommitActionViewModel> SaveButtonActions { get; }

        private PropertyWindowCommitActionViewModel _selectedApplyButtonAction;
        public PropertyWindowCommitActionViewModel SelectedApplyButtonAction
        {
            get => _selectedApplyButtonAction;
            set
            {
                if (value == null || ReferenceEquals(_selectedApplyButtonAction, value))
                    return;

                _selectedApplyButtonAction = value;
                OnPropertyChanged();
                StoreButtonSelection(OptionsConstants.PropertyWindowApplyButtonSelection, value.OptionValue);
            }
        }

        private PropertyWindowCommitActionViewModel _selectedSaveButtonAction;
        public PropertyWindowCommitActionViewModel SelectedSaveButtonAction
        {
            get => _selectedSaveButtonAction;
            set
            {
                if (value == null || ReferenceEquals(_selectedSaveButtonAction, value))
                    return;

                _selectedSaveButtonAction = value;
                OnPropertyChanged();
                StoreButtonSelection(OptionsConstants.PropertyWindowSaveButtonSelection, value.OptionValue);
            }
        }

        public BitmapImage HeaderIcon { get; }

        public string HeaderTitle { get; }

        public string HeaderDescription { get; }

        private bool _readOnly;
        public bool CanSaveChanges() => !_readOnly && PropertyViewModel?.IsDirty == true;

        private PropertyViewModel _propertyViewModel;
        public PropertyViewModel PropertyViewModel
        {
            get
            {
                return _propertyViewModel;
            }
            private set
            {
                _propertyViewModel = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<UIActionViewModel> UIActions { get; }

        public ObservableCollection<PropertyWindowSpecialExplanationEntryViewModel> SpecialExplanations { get; } = new();

        public PropertyWindowViewModel()
        {
            SaveCommand = new RelayCommand(Save, CanSaveChanges);
            ApplyCommand = new RelayCommand(Apply, CanSaveChanges);
            ApplyWithoutVerificationCommand = new RelayCommand(ApplyWithoutVerification, CanSaveChanges);
            SaveWithoutVerificationCommand = new RelayCommand(SaveWithoutVerification, CanSaveChanges);
            CancelCommand = new RelayCommand(Cancel);
            RefreshUIActionsCommand = new RelayCommand(RefreshUIActions, CanRefreshUIActions);
            ApplyButtonActions =
            [
                new PropertyWindowCommitActionViewModel
                {
                    OptionValue = ApplyOptionValue,
                    DisplayName = GetLocalizedString("Apply"),
                    IconKind = PackIconMaterialKind.Check,
                    IconColor = "#2E8B57",
                    Command = ApplyCommand
                },
                new PropertyWindowCommitActionViewModel
                {
                    OptionValue = ApplyWithoutVerificationOptionValue,
                    DisplayName = GetLocalizedString("ApplyWithoutVerification"),
                    IconKind = PackIconMaterialKind.ShieldOffOutline,
                    IconColor = "#D97706",
                    Command = ApplyWithoutVerificationCommand
                }
            ];
            SaveButtonActions =
            [
                new PropertyWindowCommitActionViewModel
                {
                    OptionValue = SaveOptionValue,
                    DisplayName = GetLocalizedString("Save"),
                    IconKind = PackIconMaterialKind.ContentSave,
                    IconColor = "#2E8B57",
                    Command = SaveCommand
                },
                new PropertyWindowCommitActionViewModel
                {
                    OptionValue = SaveWithoutVerificationOptionValue,
                    DisplayName = GetLocalizedString("SaveWithoutVerification"),
                    IconKind = PackIconMaterialKind.ContentSaveAlertOutline,
                    IconColor = "#D97706",
                    Command = SaveWithoutVerificationCommand
                }
            ];
            EnableApplySplitButton = ResolveBooleanOption(OptionsConstants.PropertyWindowEnableApplySplitButton, defaultValue: true);
            EnableSaveSplitButton = ResolveBooleanOption(OptionsConstants.PropertyWindowEnableSaveSplitButton, defaultValue: true);
            _selectedApplyButtonAction = EnableApplySplitButton
                ? ResolveButtonSelection(ApplyButtonActions, OptionsConstants.PropertyWindowApplyButtonSelection, ApplyWithoutVerificationOptionValue)
                : ApplyButtonActions.First(x => x.OptionValue == ApplyWithoutVerificationOptionValue);
            _selectedSaveButtonAction = EnableSaveSplitButton
                ? ResolveButtonSelection(SaveButtonActions, OptionsConstants.PropertyWindowSaveButtonSelection, SaveOptionValue)
                : SaveButtonActions.First(x => x.OptionValue == SaveOptionValue);
            UIActions = new ObservableCollection<UIActionViewModel>();
        }

        private static string GetLocalizedString(string resourceName)
            => FlowBloxResourceUtil.GetLocalizedString(resourceName, typeof(Resources.PropertyWindow));

        private static PropertyWindowCommitActionViewModel ResolveButtonSelection(
            IReadOnlyList<PropertyWindowCommitActionViewModel> actions,
            string optionName,
            string defaultValue)
        {
            var selectedValue = FlowBloxOptions.GetOptionInstance().GetOption(optionName)?.Value;
            return actions.FirstOrDefault(x => string.Equals(
                       x.OptionValue,
                       selectedValue,
                       StringComparison.OrdinalIgnoreCase))
                   ?? actions.First(x => x.OptionValue == defaultValue);
        }

        private static bool ResolveBooleanOption(string optionName, bool defaultValue)
        {
            var option = FlowBloxOptions.GetOptionInstance().GetOption(optionName);
            return option?.Type == OptionElement.OptionType.Boolean && bool.TryParse(option.Value, out var value)
                ? value
                : defaultValue;
        }

        private static void StoreButtonSelection(string optionName, string value)
        {
            var options = FlowBloxOptions.GetOptionInstance();
            var option = options.GetOption(optionName);
            if (option == null || string.Equals(option.Value, value, StringComparison.Ordinal))
                return;

            option.Value = value;
            options.Save();
        }

        public PropertyWindowViewModel(MetroWindow window, PropertyWindowArgs propertyWindowArgs) : this()
        {
            _window = window;
            _readOnly = propertyWindowArgs.ReadOnly;

            var propertyViewModel = new PropertyViewModel(window);
            propertyViewModel.Open(
                propertyWindowArgs.Target,
                propertyWindowArgs.Parent,
                propertyWindowArgs.DeepCopy,
                propertyWindowArgs.ReadOnly,
                propertyWindowArgs.PreselectedProperty,
                propertyWindowArgs.PreselectedInstance,
                propertyWindowArgs.Detached);

            this.PropertyViewModel = propertyViewModel;

            SubscribeToIsDirtyChanged(propertyViewModel);

            if (propertyWindowArgs.IsNew)
                propertyViewModel.IsDirty = true;

            HeaderTitle = FlowBloxComponentHelper.GetDisplayName(propertyWindowArgs.Target);
            HeaderDescription = FlowBloxComponentHelper.GetDescription(propertyWindowArgs.Target);
            var headerIcon = FlowBloxComponentHelper.GetIcon32(propertyWindowArgs.Target);
            HeaderIcon = SkiaWpfImageHelper.ConvertToImageSource(headerIcon);
            DisplaySaveButton = propertyWindowArgs.CanSave;
            LoadSpecialExplanations(propertyWindowArgs.Target);
            _uiActionTarget = propertyWindowArgs.Target;
            FlowBloxRegistryProvider.TransactionsChanged += RegistryTransactionsChanged;
            _window.Closed += Window_Closed;
            LoadUIActions(_uiActionTarget);
        }

        private void LoadSpecialExplanations(object target)
        {
            SpecialExplanations.Clear();
            if (target == null)
                return;

            var explanationEntries = target.GetType()
                .GetCustomAttributes(typeof(FlowBloxSpecialExplanationAttribute), true)
                .OfType<FlowBloxSpecialExplanationAttribute>()
                .Select(x => new
                {
                    SpecialExplanation = x.GetResolvedSpecialExplanation(),
                    x.Icon,
                    x.Color
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.SpecialExplanation))
                .ToList();

            foreach (var x in explanationEntries)
            {
                var (iconKind, iconForeground) = ResolveSpecialExplanationIcon(x.Icon, x.Color);
                SpecialExplanations.Add(new PropertyWindowSpecialExplanationEntryViewModel
                {
                    Explanation = x.SpecialExplanation,
                    OpenExplanationCommand = new RelayCommand(_ =>
                    {
                        var fullText = (x.SpecialExplanation ?? string.Empty)
                            .Replace("$$CONTINUE$$", string.Empty, StringComparison.Ordinal)
                            .Trim();
                        var displayWindow = new DisplayContentView(fullText);
                        displayWindow.Owner = _window;
                        displayWindow.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
                        displayWindow.ShowDialog();
                    }),
                    IconKind = iconKind,
                    IconForeground = iconForeground
                });
            }
        }

        private static (PackIconMaterialKind IconKind, string Foreground) ResolveSpecialExplanationIcon(
            SpecialExplanationIcon icon,
            string color)
        {
            var defaultValue = icon switch
            {
                SpecialExplanationIcon.Hint => (PackIconMaterialKind.LightbulbOnOutline, "#D97706"),
                SpecialExplanationIcon.Warning => (PackIconMaterialKind.AlertCircleOutline, "#B91C1C"),
                SpecialExplanationIcon.Success => (PackIconMaterialKind.CheckCircleOutline, "#2E7D32"),
                SpecialExplanationIcon.Important => (PackIconMaterialKind.AlertOctagonOutline, "#C2410C"),
                _ => (PackIconMaterialKind.InformationOutline, "#3A6EA5")
            };

            if (!string.IsNullOrWhiteSpace(color))
            {
                return (defaultValue.Item1, color);
            }

            return defaultValue;
        }

        private void SubscribeToIsDirtyChanged(PropertyViewModel propertyViewModel)
        {
            propertyViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PropertyViewModel.IsDirty))
                {
                    SaveCommand.Invalidate();
                    ApplyCommand.Invalidate();
                    ApplyWithoutVerificationCommand.Invalidate();
                    SaveWithoutVerificationCommand.Invalidate();
                    RequestUIActionsRefresh();
                }

            };
        }

        private bool CanRefreshUIActions()
        {
            return _uiActionTarget is IFlowBloxComponent;
        }

        private void RefreshUIActions()
        {
            StopUIActionsRefreshDebounceTimer();
            LoadUIActions(_uiActionTarget);
        }

        private void RegistryTransactionsChanged(object sender, EventArgs e)
        {
            if (_isClosed || _window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
                return;

            if (_window.Dispatcher.CheckAccess())
                HandleTransactionContextChanged();
            else
                _window.Dispatcher.BeginInvoke(HandleTransactionContextChanged);
        }

        private void HandleTransactionContextChanged()
        {
            RefreshUIActionsCommand.Invalidate();
            RequestUIActionsRefresh();
        }

        private void RequestUIActionsRefresh()
        {
            if (_isClosed ||
                _uiActionTarget is not IFlowBloxComponent)
                return;

            _uiActionsRefreshDebounceTimer ??= CreateUIActionsRefreshDebounceTimer();
            _uiActionsRefreshDebounceTimer.Stop();
            _uiActionsRefreshDebounceTimer.Start();
        }

        private DispatcherTimer CreateUIActionsRefreshDebounceTimer()
        {
            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, _window.Dispatcher)
            {
                Interval = UIActionsRefreshDebounceInterval
            };
            timer.Tick += UIActionsRefreshDebounceTimer_Tick;
            return timer;
        }

        private void UIActionsRefreshDebounceTimer_Tick(object sender, EventArgs e)
        {
            StopUIActionsRefreshDebounceTimer();
            if (_isClosed)
                return;

            LoadUIActions(_uiActionTarget);
        }

        private void StopUIActionsRefreshDebounceTimer()
            => _uiActionsRefreshDebounceTimer?.Stop();

        private void LoadUIActions(object target)
        {
            if (target is not IFlowBloxComponent component)
                return;

            List<UIActionViewModel> actions;

            var provider = new WpfUIActionsProvider();
            if (_isClosed)
                return;

            try
            {
                actions = provider.GetToolStripItemsForComponent(component);
            }
            catch(Exception e)
            {
                FlowBloxLogManager.Instance.GetLogger().Exception(e);

                _ = MessageBoxHelper.ShowMessageBoxAsync(
                    _window,
                    MessageBoxType.Error,
                    FlowBloxResourceUtil.GetLocalizedString("Message_ComponentActionsLoadingFailure", typeof(Resources.PropertyWindow)));

                return;
            }

            UIActions.Clear();
            foreach (var action in actions)
                UIActions.Add(action);
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            _isClosed = true;
            StopUIActionsRefreshDebounceTimer();
            FlowBloxRegistryProvider.TransactionsChanged -= RegistryTransactionsChanged;
            _window.Closed -= Window_Closed;
        }

        private async void Save()
        {
            var success = await _propertyViewModel.SaveAsync(_window);
            if (success)
            {
                _window.DialogResult = true;
                _window.Close();
            }
        }

        private async void Apply()
        {
            await _propertyViewModel.ApplyAsync(_window);
        }

        private async void ApplyWithoutVerification()
        {
            await _propertyViewModel.ApplyAsync(_window, true);
        }

        private async void SaveWithoutVerification()
        {
            var success = await _propertyViewModel.SaveAsync(_window, true);
            if (success)
            {
                _window.DialogResult = true;
                _window.Close();
            }
        }

        private void Cancel()
        {
            _window.DialogResult = false;
            _window.Close();
        }

        public void HandleClosing(object sender, CancelEventArgs e)
            => _propertyViewModel.HandleClosing(sender, e);

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName]string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
