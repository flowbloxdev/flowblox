using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.PropertyView;
using MahApps.Metro.IconPacks;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ListViewSplitModeFactoryResources = FlowBlox.UICore.Resources.ClassResources.ListViewSplitModeFactory;
using PropertyWindowResources = FlowBlox.UICore.Resources.PropertyWindow;

namespace FlowBlox.UICore.Factory.PropertyView
{
    public class ListViewSplitModeFactory : ListViewFactory, IPropertyViewNestedTransaction
    {
        private ListView _listView;
        private Views.PropertyView _propertyView;
        private PropertyViewModel _propertyViewModel;
        private System.Windows.Controls.Grid _mainGrid;
        private TextBlock _noSelectionText;
        private Button _saveButton;
        private Button _applyWithoutVerificationButton;
        private bool _restoringSelection;
        private object _selectedItem;

        public bool HasPendingChanges => _propertyViewModel?.IsDirty == true;

        public ListViewSplitModeFactory(Window window, PropertyInfo property, object target, bool readOnly, object parent = null)
            : base(window, property, target, readOnly, parent)
        {
        }

        protected override bool ShouldShowEditButton => false;

        protected override FrameworkElement CreateFrameworkElement(StackPanel stackPanel, ListView listView)
        {
            _listView = listView;
            listView.SelectionChanged += ListView_SelectionChanged;
            listView.HorizontalAlignment = HorizontalAlignment.Stretch;
            listView.VerticalAlignment = VerticalAlignment.Stretch;

            // In split mode, avoid StackPanel sizing-to-content behavior on the left side.
            // Rehost toolbar + list container in a docked grid (Auto + *) so the list fills available space.
            var leftPanel = BuildDockedLeftPanel(stackPanel);

            // Prepare PropertyView
            _propertyViewModel = new PropertyViewModel(_window);
            _propertyViewModel.PropertyChanged += PropertyViewModel_PropertyChanged;
            _propertyView = new Views.PropertyView
            {
                DataContext = _propertyViewModel
            };

            // NoSelectionText
            _noSelectionText = new TextBlock
            {
                Text = GetLocalizedString(nameof(ListViewSplitModeFactoryResources.NoElementSelectedText)),
                FontSize = 16,
                FontWeight = FontWeights.UltraLight,
                Foreground = Brushes.Gray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Visibility = Visibility.Visible
            };

            // Use the same commit actions as the Property Window, without dropdowns.
            var saveButton = CreateApplyButton(withoutVerification: false);
            _saveButton = saveButton;
            _applyWithoutVerificationButton = CreateApplyButton(withoutVerification: true);
            saveButton.Click += async (s, e) => await ApplyChangesAsync(withoutVerification: false);
            _applyWithoutVerificationButton.Click += async (s, e) => await ApplyChangesAsync(withoutVerification: true);

            // Create a 2-row Grid for PropertyView and bottom toolbar.
            var rightGrid = new System.Windows.Controls.Grid()
            {
                VerticalAlignment = VerticalAlignment.Stretch
            };
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var applyButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 5, 8)
            };
            _applyWithoutVerificationButton.Margin = new Thickness(0, 0, 8, 0);
            applyButtons.Children.Add(_applyWithoutVerificationButton);
            applyButtons.Children.Add(saveButton);
            System.Windows.Controls.Grid.SetRow(applyButtons, 1);
            rightGrid.Children.Add(applyButtons);

            System.Windows.Controls.Grid.SetRow(_propertyView, 0);
            System.Windows.Controls.Grid.SetRow(_noSelectionText, 0);
            rightGrid.Children.Add(_propertyView);
            rightGrid.Children.Add(_noSelectionText);

            // Now put everything in the main horizontal Grid (2 Columns)
            _mainGrid = new System.Windows.Controls.Grid();
            _mainGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            _mainGrid.VerticalAlignment = VerticalAlignment.Stretch;
            _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // List
            _mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) }); // Property

            System.Windows.Controls.Grid.SetColumn(leftPanel, 0);
            System.Windows.Controls.Grid.SetColumn(rightGrid, 1);

            _mainGrid.Children.Add(leftPanel);
            _mainGrid.Children.Add(rightGrid);

            return _mainGrid;
        }

        private static System.Windows.Controls.Grid BuildDockedLeftPanel(StackPanel stackPanel)
        {
            var leftGrid = new System.Windows.Controls.Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            if (stackPanel.Children.Count > 0)
            {
                var toolbar = stackPanel.Children[0] as UIElement;
                if (toolbar != null)
                {
                    stackPanel.Children.RemoveAt(0);
                    var toolbarHost = CreateEmbeddedToolbarHost(toolbar);
                    System.Windows.Controls.Grid.SetRow(toolbarHost, 0);
                    leftGrid.Children.Add(toolbarHost);
                }
            }

            if (stackPanel.Children.Count > 0)
            {
                var listContainer = stackPanel.Children[0] as UIElement;
                if (listContainer != null)
                {
                    stackPanel.Children.RemoveAt(0);
                    if (listContainer is System.Windows.Controls.Grid containerGrid
                        && containerGrid.RowDefinitions.Count > 0)
                    {
                        containerGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                        containerGrid.VerticalAlignment = VerticalAlignment.Stretch;
                        containerGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
                    }

                    System.Windows.Controls.Grid.SetRow(listContainer, 1);
                    leftGrid.Children.Add(listContainer);
                }
            }

            return leftGrid;
        }

        private static Border CreateEmbeddedToolbarHost(UIElement toolbar, bool compact = false)
        {
            var verticalPadding = compact ? 3 : 4;
            return new Border
            {
                Background = (Brush)new BrushConverter().ConvertFromString("#DDE7F3"),
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(6, verticalPadding, 6, verticalPadding),
                Child = toolbar
            };
        }

        private async void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_restoringSelection)
                return;

            var selectedItem = _listView.SelectedItem;
            if (!await ConfirmAndCloseCurrentDetailAsync())
            {
                RestoreSelection();
                return;
            }

            _selectedItem = selectedItem;

            if (selectedItem != null)
            {
                _noSelectionText.Visibility = Visibility.Collapsed;
                _propertyView.Visibility = Visibility.Visible;
                _propertyViewModel.Open(
                    selectedItem,
                    _target,
                    deepCopy: true,
                    readOnly: _readOnly,
                    nestedTransaction: true);
                _propertyViewModel.IsDirty = false;
                UpdateSaveButtonState();
            }
            else if (_propertyViewModel != null)
            {
                _propertyViewModel.Cancel(keepComponent: true);
                _propertyView.Visibility = Visibility.Collapsed;
                _noSelectionText.Visibility = Visibility.Visible;
                UpdateSaveButtonState();
            }
        }

        private void RestoreSelection()
        {
            _restoringSelection = true;
            _listView.SelectedItem = _selectedItem;
            _restoringSelection = false;
        }

        private async Task<bool> ConfirmAndCloseCurrentDetailAsync()
        {
            if (_propertyViewModel?.HasActiveTransaction != true)
                return true;

            if (_propertyViewModel.IsDirty)
            {
                if (_window is not MahApps.Metro.Controls.MetroWindow metroWindow)
                    return false;

                var discardChanges = await MessageBoxHelper.ShowQuestionAsync(
                    metroWindow,
                    GetLocalizedString(nameof(ListViewSplitModeFactoryResources.DiscardChangesTitle)),
                    GetLocalizedString(nameof(ListViewSplitModeFactoryResources.DiscardChangesMessage)),
                    GetLocalizedString(nameof(ListViewSplitModeFactoryResources.DiscardChangesButton)),
                    GetLocalizedString(nameof(ListViewSplitModeFactoryResources.ContinueEditingButton)));
                if (discardChanges != true)
                    return false;
            }

            CloseCurrentDetail();
            return true;
        }

        private void PropertyViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PropertyViewModel.IsDirty))
            {
                UpdateSaveButtonState();
                OnPropertyChanged(nameof(HasPendingChanges));
            }
        }

        public async Task<bool> PrepareHostCommitAsync(MahApps.Metro.Controls.MetroWindow window, bool withoutVerification)
        {
            if (_propertyViewModel?.HasActiveTransaction != true)
                return true;

            if (!HasPendingChanges)
            {
                _propertyViewModel.Cancel();
                return true;
            }

            var success = await _propertyViewModel.SaveAsync(window, withoutVerification);
            if (success)
                _propertyViewModel.IsDirty = false;

            return success;
        }

        public void Cancel()
        {
            CloseCurrentDetail();

            _restoringSelection = true;
            if (_listView != null)
                _listView.UnselectAll();
            _restoringSelection = false;

            _selectedItem = null;

            if (_propertyView != null)
                _propertyView.Visibility = Visibility.Collapsed;
            if (_noSelectionText != null)
                _noSelectionText.Visibility = Visibility.Visible;

            UpdateSaveButtonState();
        }

        private void CloseCurrentDetail()
        {
            if (_propertyViewModel?.HasActiveTransaction == true)
                _propertyViewModel.Cancel(keepComponent: true);

            if (_propertyViewModel != null)
                _propertyViewModel.IsDirty = false;

            UpdateSaveButtonState();
            OnPropertyChanged(nameof(HasPendingChanges));
        }

        private void UpdateSaveButtonState()
        {
            var enabled = _listView?.SelectedItem != null
                && !_readOnly
                && _propertyViewModel?.IsDirty == true;
            if (_saveButton != null)
                _saveButton.IsEnabled = enabled;
            if (_applyWithoutVerificationButton != null)
                _applyWithoutVerificationButton.IsEnabled = enabled;
        }

        private async Task ApplyChangesAsync(bool withoutVerification)
        {
            if (_readOnly || !HasPendingChanges || _window is not MahApps.Metro.Controls.MetroWindow metro)
                return;

            if (await _propertyViewModel.SaveAsync(metro, withoutVerification))
            {
                _propertyViewModel.Open(
                    _listView.SelectedItem,
                    _target,
                    deepCopy: true,
                    readOnly: _readOnly,
                    nestedTransaction: true);
                _propertyViewModel.IsDirty = false;
                UpdateSaveButtonState();
            }
        }

        protected override async void ExecuteCreate()
        {
            if (_readOnly)
                return;

            if (!await ConfirmAndCloseCurrentDetailAsync())
                return;

            var newInstance = CreateNewInstance(_window, _listItemType);

            if (newInstance != null)
            {
                _list.Add(newInstance);
                _property.SetValue(_target, _list);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);

                if (_listView != null)
                    _listView.SelectedItem = newInstance;

                UpdateSaveButtonState();
            }
        }

        protected override async Task ExecuteDeleteItemsAsync(IReadOnlyList<object> items)
        {
            if (!await ConfirmAndCloseCurrentDetailAsync())
            {
                RestoreSelection();
                return;
            }

            _restoringSelection = true;
            try
            {
                await base.ExecuteDeleteItemsAsync(items);
            }
            finally
            {
                _restoringSelection = false;
            }

            Cancel();
        }

        private Button CreateApplyButton(bool withoutVerification)
        {
            var button = new Button
            {
                IsEnabled = false,
                MinWidth = 112,
                MinHeight = 32,
                Padding = new Thickness(12, 5, 12, 5),
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new PackIconMaterial
                        {
                            Kind = withoutVerification ? PackIconMaterialKind.ShieldOffOutline : PackIconMaterialKind.Check,
                            Width = 14,
                            Height = 14,
                            Margin = new Thickness(0, 0, 8, 0),
                            Foreground = (Brush)new BrushConverter().ConvertFromString(withoutVerification ? "#D97706" : "#2E8B57"),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = FlowBloxResourceUtil.GetLocalizedString(
                                withoutVerification ? nameof(PropertyWindowResources.ApplyWithoutVerification) : nameof(PropertyWindowResources.Apply),
                                typeof(PropertyWindowResources)),
                            VerticalAlignment = VerticalAlignment.Center
                        }
                    }
                }
            };

            return button;
        }

        private static string GetLocalizedString(string key)
            => FlowBloxResourceUtil.GetLocalizedString(key, typeof(ListViewSplitModeFactoryResources));
    }
}
