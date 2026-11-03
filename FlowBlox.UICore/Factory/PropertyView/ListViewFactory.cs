using ListViewFactoryResources = FlowBlox.UICore.Resources.ClassResources.ListViewFactory;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Converters;
using FlowBlox.UICore.Converters.PropertyView;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FlowBlox.UICore.Factory.PropertyView
{
    public class ListViewFactory : ListFactoryBase
    {
        private static readonly FlowBloxComponentIcon16Converter Icon16Converter = new();
        protected readonly FlowBloxListViewAttribute _listViewAttribute;
        protected IList _list;
        protected Type _listItemType;
        protected readonly object _parent;
        private IDialogService _dialogService;
        protected FrameworkElement _emptyMessage;

        public ListViewFactory(Window window, PropertyInfo property, object target, bool readOnly, object parent = null)
            : base(window, property, target, readOnly)
        {
            _parent = parent;
            _listViewAttribute = property.GetCustomAttribute<FlowBloxListViewAttribute>()
                ?? throw new InvalidOperationException("Missing FlowBlockListViewAttribute.");

            if (!typeof(IList).IsAssignableFrom(_property.PropertyType))
                throw new InvalidOperationException("Property is not IList.");

            _list = (IList)_property.GetValue(_target) ?? (IList)Activator.CreateInstance(_property.PropertyType);
            _listItemType = _property.PropertyType.GetGenericArguments()[0];
            _dialogService = FlowBloxServiceLocator.Instance.GetService<IDialogService>();
        }

        public FrameworkElement Create()
        {
            var listView = new ListView
            {
                Margin = new Thickness(0, 5, 0, 10)
            };
            ParentScrollViewerMouseWheelForwarder.Register(listView);

            var gridView = new GridView();
            listView.View = gridView;

            var iconFactory = new FrameworkElementFactory(typeof(Image));
            iconFactory.SetValue(Image.WidthProperty, 16.0);
            iconFactory.SetValue(Image.HeightProperty, 16.0);
            iconFactory.SetValue(Image.MarginProperty, new Thickness(0, 0, 6, 0));
            iconFactory.SetValue(Image.VerticalAlignmentProperty, VerticalAlignment.Center);
            iconFactory.SetBinding(Image.SourceProperty, new Binding(".")
            {
                Converter = Icon16Converter
            });

            var iconTemplate = new DataTemplate
            {
                VisualTree = iconFactory
            };

            gridView.Columns.Add(new GridViewColumn
            {
                Header = string.Empty,
                Width = 32,
                CellTemplate = iconTemplate
            });

            var listViewProperties = _listItemType
                .GetProperties()
                .Where(x => _listViewAttribute.LVColumnMemberNames.Contains(x.Name));

            foreach (var prop in listViewProperties)
            {
                var header = prop.GetCustomAttribute<DisplayAttribute>() is DisplayAttribute displayAttr
                    ? FlowBloxResourceUtil.GetDisplayName(displayAttr)
                    : prop.Name;

                var template = new DataTemplate();

                if (prop.PropertyType == typeof(bool))
                {
                    var factory = new FrameworkElementFactory(typeof(PackIconMaterial));
                    factory.SetBinding(PackIconMaterial.KindProperty, new Binding(prop.Name)
                    {
                        Converter = new BooleanToPackIconKindConverter()
                    });
                    factory.SetValue(PackIconMaterial.WidthProperty, 14.0);
                    factory.SetValue(PackIconMaterial.HeightProperty, 14.0);
                    factory.SetValue(PackIconMaterial.ForegroundProperty, Brushes.Gray);

                    template.VisualTree = factory;
                }
                else
                {
                    var factory = new FrameworkElementFactory(typeof(TextBlock));
                    factory.SetValue(TextBlock.MinWidthProperty, 150.0);
                    factory.SetBinding(TextBlock.TextProperty, new Binding(prop.Name));
                    template.VisualTree = factory;
                }

                gridView.Columns.Add(new GridViewColumn
                {
                    Header = header,
                    CellTemplate = template
                });
            }

            listView.ItemsSource = _list;

            // Set selection to preselectedInstance (if available)
            var preselectedInstance = ResolvePreselectedInstanceInCurrentTransaction(_list);
            if (preselectedInstance != null && _list.Contains(preselectedInstance))
            {
                listView.SelectedItem = preselectedInstance;
                listView.Dispatcher.InvokeAsync(() =>
                {
                    listView.ScrollIntoView(preselectedInstance);
                }, DispatcherPriority.ApplicationIdle);
            }

            _emptyMessage = CreateEmptyMessage(_list);

            var stackPanel = new StackPanel();
            var toolBar = new ToolBar();

            var container = new System.Windows.Controls.Grid();
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            container.Children.Add(listView);
            container.Children.Add(_emptyMessage);

            System.Windows.Controls.Grid.SetRow(listView, 0);
            System.Windows.Controls.Grid.SetRow(_emptyMessage, 0);

            stackPanel.Children.Add(toolBar);
            stackPanel.Children.Add(container);

            // RelayCommands with central enable logic
            var addCommand = new RelayCommand(ExecuteCreate, CanAdd);
            var editCommand = new RelayCommand(() => ExecuteEdit(listView.SelectedItem),
                () => _uiAttribute?.Operations.HasFlag(UIOperations.Edit) == true && CanEdit(listView.SelectedItem));
            listView.MouseDoubleClick += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left)
                    return;

                var row = ItemsControl.ContainerFromElement(listView, e.OriginalSource as DependencyObject) as ListViewItem;
                if (row == null || !ReferenceEquals(row.Content, listView.SelectedItem) || !editCommand.CanExecute(null))
                    return;

                e.Handled = true;
                editCommand.Execute(null);
            };
            var deleteCommand = new RelayCommand(
                async () => await ExecuteDeleteItemsAsync(listView.SelectedItems.Cast<object>().ToList()),
                () => listView.SelectedItems.Count > 0 && !_readOnly);
            var unlinkCommand = new RelayCommand(() => ExecuteUnlink(listView.SelectedItem), () => CanUnlink(listView.SelectedItem));
            var linkCommand = new RelayCommand(async () => await ExecuteLink(), CanLink);

            if (_uiAttribute?.Operations.HasFlag(UIOperations.Create) == true)
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.Plus, FlowBloxResourceUtil.GetLocalizedString("Buttons_Add", typeof(ListViewFactoryResources)), addCommand, Brushes.Green));

            if (_uiAttribute?.Operations.HasFlag(UIOperations.Link) == true)
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.Link, FlowBloxResourceUtil.GetLocalizedString("Buttons_Link", typeof(ListViewFactoryResources)), linkCommand, Brushes.Blue));

            if (ShouldShowEditButton && _uiAttribute?.Operations.HasFlag(UIOperations.Edit) == true)
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.Pencil, FlowBloxResourceUtil.GetLocalizedString("Buttons_Edit", typeof(ListViewFactoryResources)), editCommand, Brushes.Orange));

            if (_uiAttribute?.Operations.HasFlag(UIOperations.Delete) == true)
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.Delete, FlowBloxResourceUtil.GetLocalizedString("Buttons_Remove", typeof(ListViewFactoryResources)), deleteCommand, Brushes.DarkRed));

            if (_uiAttribute?.Operations.HasFlag(UIOperations.Unlink) == true)
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.LinkOff, FlowBloxResourceUtil.GetLocalizedString("Buttons_Unlink", typeof(ListViewFactoryResources)), unlinkCommand, Brushes.DarkRed));

            var moveUpCommand = new RelayCommand(() => MoveItem(listView, -1), () => CanMoveUp(listView.SelectedIndex));
            var moveDownCommand = new RelayCommand(() => MoveItem(listView, 1), () => CanMoveDown(listView.SelectedIndex));

            if (_listViewAttribute?.IsMovable == true)
            {
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.ArrowUp, FlowBloxResourceUtil.GetLocalizedString("Buttons_MoveUp", typeof(ListViewFactoryResources)), moveUpCommand, Brushes.Blue));
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.ArrowDown, FlowBloxResourceUtil.GetLocalizedString("Buttons_MoveDown", typeof(ListViewFactoryResources)), moveDownCommand, Brushes.Blue));
            }

            // RelayCommands with enable logic
            listView.SelectionChanged += (s, e) =>
            {
                editCommand.Invalidate();
                deleteCommand.Invalidate();
                unlinkCommand.Invalidate();
                moveUpCommand.Invalidate();
                moveDownCommand.Invalidate();
            };

            UpdateEmptyMessageVisibilityOnCollectionChanged(_emptyMessage, _list);

            var frameworkElement = CreateFrameworkElement(stackPanel, listView);
            return ResizableControlContainer.Create(frameworkElement);
        }

        protected virtual FrameworkElement CreateFrameworkElement(StackPanel stackPanel, ListView listView)
        {
            return stackPanel;
        }

        protected virtual bool CanAdd() => !_readOnly;

        protected virtual bool ShouldShowEditButton => true;

        protected virtual bool CanEdit(object selectedItem) => selectedItem != null;

        protected virtual bool CanRemove(object selectedItem) => selectedItem != null && !_readOnly;

        protected virtual bool CanLink() => !_readOnly && !FlowBloxRegistryProvider.IsCurrentlyDetached;

        protected virtual bool CanUnlink(object selectedItem) =>
            selectedItem != null &&
            !_readOnly &&
            !FlowBloxRegistryProvider.IsCurrentlyDetached;

        protected virtual bool CanMoveUp(int selectedIndex) => selectedIndex > 0 && !_readOnly;

        protected virtual bool CanMoveDown(int selectedIndex) =>
            selectedIndex >= 0 &&
            selectedIndex < _list.Count - 1 &&
            !_readOnly;

        private Button CreateButton(PackIconMaterialKind iconKind, string tooltip, RelayCommand command, Brush activeColor)
        {
            var button = new Button
            {
                ToolTip = tooltip,
                Command = command,
                Content = new PackIconMaterial
                {
                    Kind = iconKind,
                    Width = 12,
                    Height = 12
                },
                Width = 24,
                Height = 24,
                Margin = new Thickness(5, 0, 0, 0)
            };

            // Set style with active and disabled color
            ButtonStyleHelper.SetButtonStyle(button, activeColor, Brushes.Gray);

            return button;
        }

        protected override Type GetExplicitItemFactoryType(Type type)
        {
            return _listItemType.IsAssignableFrom(type) ? _listViewAttribute.ItemFactory : null;
        }

        protected virtual void ExecuteCreate()
        {
            var newInstance = CreateNewInstance(_window, _listItemType);
            if (newInstance != null)
            {
                var success = WpfPropertyWindowProvider.CreatePropertyWindowAndShowDialog(_window, _target, newInstance, _readOnly, isNew: true);
                if (success)
                {
                    _list.Add(newInstance);
                    _property.SetValue(_target, _list);
                    FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                }
                else
                {
                    DeleteInstance(newInstance);
                }
            }
        }

        private async Task ExecuteLink()
        {
            if (string.IsNullOrEmpty(_uiAttribute?.SelectionFilterMethod))
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("MissingFilterMethod_Title", typeof(ListViewFactoryResources)),
                    string.Format(FlowBloxResourceUtil.GetLocalizedString("MissingFilterMethod_Message", typeof(ListViewFactoryResources)), _property.Name));
                return;
            }

            var selectionMethodResolution = SelectionMethodResolver.ResolveSelectionFilterMethodFromTargetOrParent(
                _target,
                _parent,
                _uiAttribute.SelectionFilterMethod);

            if (selectionMethodResolution?.Method == null)
                return;

            var items = selectionMethodResolution.Method.Invoke(selectionMethodResolution.InvocationTarget, null) as IList;
            if (items == null || items.Count == 0)
            {
                await MessageBoxHelper.ShowMessageBoxAsync(
                    (MetroWindow)_window,
                    FlowBloxResourceUtil.GetLocalizedString("NoItemsFound_Title", typeof(ListViewFactoryResources)),
                    FlowBloxResourceUtil.GetLocalizedString("NoItemsFound_Message", typeof(ListViewFactoryResources)));
                return;
            }

            if (_listItemType == typeof(FieldElement))
            {
                var fieldSelectionAttribute = _property.GetCustomAttribute<FlowBloxFieldSelectionAttribute>();
                var args = Utilities.TextBoxHelper.CreateFieldSelectionWindowArgs(_target, fieldSelectionAttribute);
                args.FieldElements = items.OfType<FieldElement>();
                args.SelectionMode = FieldSelectionMode.Fields;
                args.AllowedFieldSelectionModes = [FieldSelectionMode.Fields];

                var fieldSelectionResult = Utilities.TextBoxHelper.ShowFieldSelectionDialog(args, _window);
                if (fieldSelectionResult != null)
                {
                    FlowBlockHelper.ApplyFieldSelectionRequiredOption(
                        _target,
                        fieldSelectionResult.SelectedFields,
                        fieldSelectionResult.IsRequired);

                    var inst = fieldSelectionResult.SelectedFields.Single();
                    TryAddLinkedObject(_property.Name, inst);
                }
            }
            else
            {
                var dialog = new MultiValueSelectionDialog(
                    FlowBloxResourceUtil.GetLocalizedString("SelectionDialog_Title", typeof(ListViewFactoryResources)),
                    FlowBloxResourceUtil.GetLocalizedString("SelectionDialog_Message", typeof(ListViewFactoryResources)),
                    new GenericSelectionHandler<object>(items.Cast<object>(), x => x.ToString()))
                {
                    Owner = _window,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                if (dialog.ShowDialog() == true)
                {
                    TryAddLinkedObject(_property.Name, dialog.SelectedItem.Value);
                }
            }
        }

        protected virtual bool TryAddLinkedObject(string propertyName, object selectedObject)
        {
            var beforeLinkResult = ProcessAssociationBeforeLink(propertyName, selectedObject);
            if (beforeLinkResult.Cancel)
                return false;

            var linkedObject = beforeLinkResult.LinkedObject;
            if (linkedObject == null)
                return false;

            if (!_listItemType.IsInstanceOfType(linkedObject))
            {
                throw new InvalidOperationException(
                    $"AssociationBeforeLink returned an object of type '{linkedObject.GetType().FullName}', " +
                    $"but the list expects instances of type '{_listItemType.FullName}'.");
            }

            _list.Add(linkedObject);
            _property.SetValue(_target, _list);
            FlowBloxComponentHelper.RaisePropertyChanged(_target, propertyName);

            return true;
        }

        protected virtual void ExecuteEdit(object item)
        {
            if (item != null)
            {
                var success = WpfPropertyWindowProvider.CreatePropertyWindowAndShowDialog(_window, _target, item, _readOnly);
                if (success)
                    FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }
        }

        protected virtual async Task ExecuteDeleteAsync(object item)
        {
            if (item != null && await ConfirmDeletionAsync(item, _window))
            {
                _list.Remove(item);
                _property.SetValue(_target, _list);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                DeleteInstance(item);
            }
        }

        protected virtual async Task ExecuteDeleteItemsAsync(IReadOnlyList<object> items)
        {
            foreach (var item in items)
                await ExecuteDeleteAsync(item);
        }

        private void ExecuteUnlink(object item)
        {
            if (item != null)
            {
                _list.Remove(item);
                _property.SetValue(_target, _list);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }
        }

        private void MoveItem(ListView listView, int direction)
        {
            int index = listView.SelectedIndex;
            if (index < 0 || (index == 0 && direction == -1) || (index == _list.Count - 1 && direction == 1)) 
                return;

            var item = _list[index];
            _list.RemoveAt(index);
            _list.Insert(index + direction, item);

            listView.SelectedIndex = index + direction;
            FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
        }
    }
}
