using AssociationControlFactoryResources = FlowBlox.UICore.Resources.ClassResources.AssociationControlFactory;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Converters.PropertyView;
using FlowBlox.UICore.Factory.PropertyView.HintTextResolver;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace FlowBlox.UICore.Factory.PropertyView
{
    public class AssociationControlFactory : WpfPropertyViewControlFactory
    {
        private readonly RelayCommand _createCommand;
        private readonly RelayCommand _linkCommand;
        private readonly RelayCommand _unlinkCommand;
        private readonly RelayCommand _editCommand;
        private readonly RelayCommand _deleteCommand;
        private readonly object _parent;

        private TextBox _textBox;

        public AssociationControlFactory(Window window, PropertyInfo property, object target, bool readOnly, object parent = null)
            : base(window, property, target, readOnly)
        {
            _parent = parent;
            _createCommand = new RelayCommand(ExecuteCreate, CanCreate);
            _linkCommand = new RelayCommand(ExecuteLink, CanLink);
            _unlinkCommand = new RelayCommand(ExecuteUnlink, CanUnlink);
            _editCommand = new RelayCommand(ExecuteEdit, CanEdit);
            _deleteCommand = new RelayCommand(ExecuteDelete, CanDelete);
        }

        protected override Type GetExplicitItemFactoryType(Type type)
        {
            return _property.PropertyType.IsAssignableFrom(type) ? _associationAttribute?.ItemFactory : null;
        }

        private string _resolvedFlowBlockHintText;
        public string ResolvedFlowBlockHintText
        {
            get => _resolvedFlowBlockHintText;
            private set
            {
                if (_resolvedFlowBlockHintText != value)
                {
                    _resolvedFlowBlockHintText = value;
                    OnPropertyChanged(nameof(ResolvedFlowBlockHintText));
                }
            }
        }

        private string ComputeResolvedFlowBlockHintText()
        {
            var fbResolvable = _property.GetCustomAttribute<AssociatedFlowBlockResolvableAttribute>();
            if (fbResolvable != null)
            {
                var resolver = new AssociatedFlowBlockResolvableHintTextResolver(_target, _property);
                return resolver.ResolveHintText(fbResolvable);
            }

            var customResolvable = _property.GetCustomAttribute<AssociatedFlowBlockResolvableCustomAttribute>();
            if (customResolvable != null)
            {
                var resolver = new AssociatedFlowBlockResolvableCustomHintTextResolver(_target, _property);
                return resolver.ResolveHintText(customResolvable);
            }

            return null;
        }

        private void AppendResolvedFlowBlockHint(StackPanel stackPanel)
        {
            var textBlock = new TextBlock
            {
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.Gray,
                Margin = new Thickness(5, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };

            var binding = new Binding(nameof(ResolvedFlowBlockHintText))
            {
                Source = this
            };

            textBlock.SetBinding(TextBlock.TextProperty, binding);
            stackPanel.Children.Add(textBlock);
            UpdateResolvedFlowBlockHintText();
            TryRegisterTargetPropertyListener();
        }

        private void TryRegisterTargetPropertyListener()
        {
            if (_target is INotifyPropertyChanged notifier)
                notifier.PropertyChanged += TargetOnPropertyChanged;
        }

        private void TargetOnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == _property.Name || 
                e.PropertyName == nameof(BaseFlowBlock.ReferencedFlowBlocks))
            {
                UpdateResolvedFlowBlockHintText();
            }
        }

        private void UpdateResolvedFlowBlockHintText()
        {
            ResolvedFlowBlockHintText = ComputeResolvedFlowBlockHintText();
        }

        public StackPanel Create()
        {
            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(5)
            };

            _textBox = new TextBox
            {
                IsReadOnly = true,
                Width = 200
            };

            var binding = new Binding(_property.Name)
            {
                Source = _target,
                Converter = new AssociationControlTextConverter(),
                Mode = BindingMode.OneWay
            };

            _textBox.SetBinding(TextBox.TextProperty, binding);

            stackPanel.Children.Add(new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Cube,
                Width = 14,
                Height = 14,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(33, 150, 243))
            });

            stackPanel.Children.Add(_textBox);

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Create) == true)
                stackPanel.Children.Add(CreateButton(PackIconMaterialKind.Plus, _createCommand, Brushes.Green));

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Link) == true)
                stackPanel.Children.Add(CreateButton(PackIconMaterialKind.Link, _linkCommand, Brushes.Blue));

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Unlink) == true)
                stackPanel.Children.Add(CreateButton(PackIconMaterialKind.LinkOff, _unlinkCommand, Brushes.DarkRed));

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Edit) == true)
                stackPanel.Children.Add(CreateButton(PackIconMaterialKind.Pencil, _editCommand, Brushes.Orange));

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Delete) == true)
                stackPanel.Children.Add(CreateButton(PackIconMaterialKind.Delete, _deleteCommand, Brushes.DarkRed));

            AppendResolvedFlowBlockHint(stackPanel);

            return stackPanel;
        }

        private Button CreateButton(PackIconMaterialKind iconKind, RelayCommand command, Brush activeColor)
        {
            var button = new Button
            {
                Style = (Style)_window.FindResource("MahApps.Styles.Button"),
                Content = new PackIconMaterial
                {
                    Kind = iconKind,
                    Width = 12,
                    Height = 12
                },
                Width = 24,
                Height = 24,
                Margin = new Thickness(5, 0, 0, 0),
                Command = command
            };
            ButtonStyleHelper.SetButtonStyle(button, activeColor, Brushes.Gray);
            return button;
        }

        private void ExecuteCreate()
        {
            var newInstance = CreateNewInstance(_window);
            if (newInstance != null)
            {
                var propertyView = new Views.PropertyWindow(new PropertyWindowArgs(newInstance, parent: _target, readOnly: false, isNew: true))
                {
                    Owner = _window,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                if (propertyView.ShowDialog() == true)
                {
                    _property.SetValue(_target, newInstance);
                    FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                    InvalidateAll();
                }
                else
                {
                    DeleteInstance(newInstance);
                }
            }
        }

        private async void ExecuteLink()
        {
            if (string.IsNullOrEmpty(_itemsSourceAttribute?.Method))
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("MissingFilterMethod_Title", typeof(AssociationControlFactoryResources)),
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("MissingFilterMethod_Message", typeof(AssociationControlFactoryResources)),
                        _property.Name));

                return;
            }

            var selectionMethodResolution = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(
                _target,
                _parent,
                _itemsSourceAttribute.Method);

            if (selectionMethodResolution?.Method == null)
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("FilterMethodNotFound_Title", typeof(AssociationControlFactoryResources)),
                    string.Format(
                        FlowBloxResourceUtil.GetLocalizedString("FilterMethodNotFound_Message", typeof(AssociationControlFactoryResources)),
                        _itemsSourceAttribute.Method,
                        _target.GetType().Name));

                return;
            }

            var items = selectionMethodResolution.Method.Invoke(selectionMethodResolution.InvocationTarget, null) as IEnumerable<object>;
            if (items == null || !items.Any())
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("NoItemsFound_Title", typeof(AssociationControlFactoryResources)),
                    FlowBloxResourceUtil.GetLocalizedString("NoItemsFound_Message", typeof(AssociationControlFactoryResources)));

                return;
            }

            var dialog = new MultiValueSelectionDialog(
                FlowBloxResourceUtil.GetLocalizedString("SelectionDialog_Title", typeof(AssociationControlFactoryResources)),
                FlowBloxResourceUtil.GetLocalizedString("SelectionDialog_Message", typeof(AssociationControlFactoryResources)),
                new GenericSelectionHandler<object>(items.Cast<object>(), x => x.ToString()))
            {
                Owner = _window,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (dialog.ShowDialog() == true)
            {
                if (TrySetLinkedObject(_property.Name, dialog.SelectedItem.Value))
                    InvalidateAll();
            }
        }

        protected virtual bool TrySetLinkedObject(string propertyName, object selectedObject)
        {
            var beforeLinkResult = ProcessAssociationBeforeLink(propertyName, selectedObject);
            if (beforeLinkResult.Cancel)
                return false;

            var linkedObject = beforeLinkResult.LinkedObject;
            if (linkedObject == null)
                return false;

            if (!_property.PropertyType.IsInstanceOfType(linkedObject))
            {
                throw new InvalidOperationException(
                    $"AssociationBeforeLink returned an object of type '{linkedObject.GetType().FullName}', " +
                    $"but property '{propertyName}' expects type '{_property.PropertyType.FullName}'.");
            }

            _property.SetValue(_target, linkedObject);
            FlowBloxComponentHelper.RaisePropertyChanged(_target, propertyName);

            return true;
        }

        private void ExecuteUnlink()
        {
            _property.SetValue(_target, null);
            FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            InvalidateAll();
        }

        private void ExecuteEdit()
        {
            var item = _property.GetValue(_target);
            if (item != null)
            {
                var propertyView = new Views.PropertyWindow(new PropertyWindowArgs(item, parent: _target, readOnly: _readOnly))
                {
                    Owner = _window,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                if (propertyView.ShowDialog() == true)
                    FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }
        }

        private async void ExecuteDelete()
        {
            var item = _property.GetValue(_target);
            if (item != null && await ConfirmDeletionAsync(item, _window))
            {
                _property.SetValue(_target, null);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                DeleteInstance(item);
                InvalidateAll();
            }
        }

        private bool CanEdit() => _property.GetValue(_target) != null;

        private bool CanCreate() => ! _readOnly;

        private bool CanLink() => !_readOnly && !FlowBloxRegistryProvider.IsCurrentlyDetached;

        private bool CanUnlink() =>
            _property.GetValue(_target) != null &&
            !_readOnly &&
            !FlowBloxRegistryProvider.IsCurrentlyDetached;

        private bool CanDelete() => _property.GetValue(_target) != null && !_readOnly;

        private void InvalidateAll()
        {
            _unlinkCommand.Invalidate();
            _editCommand.Invalidate();
            _deleteCommand.Invalidate();
        }
    }
}
