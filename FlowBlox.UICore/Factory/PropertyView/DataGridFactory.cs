using FlowBlox.Core.Attributes;
using FlowBlox.Core;
using FlowBlox.Core.Extensions;
using FlowBlox.Core.Interfaces;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util.Resources;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Interfaces;
using DataGridFactoryResources = FlowBlox.UICore.Resources.ClassResources.DataGridFactory;
using FlowBlox.UICore.Factory.Adapter;
using FlowBlox.UICore.PropertyView.Converters;
using FlowBlox.UICore.PropertyView.Resolver;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.Views;
using MahApps.Metro.IconPacks;
using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util;
using FlowBlox.UICore.Resolver;

namespace FlowBlox.UICore.Factory.PropertyView
{
    public class DataGridFactory : ListFactoryBase
    {
        private const double DefaultTextColumnMinWidth = 140;

        private readonly FlowBloxDataGridAttribute _dataGridAttribute;
        private readonly object _parent;

        private RelayCommand _addCommand;
        private RelayCommand _deleteCommand;
        private RelayCommand _editCommand;
        private RelayCommand _moveUpCommand;
        private RelayCommand _moveDownCommand;

        public DataGridFactory(Window window, PropertyInfo property, object target, bool readOnly, object parent = null)
            : base(window, property, target, readOnly)
        {
            _parent = parent;
            _dataGridAttribute = _property.GetCustomAttribute<FlowBloxDataGridAttribute>();
        }

        private void SetCellStyle(DataGrid dataGrid)
        {
            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            var trigger = new Trigger
            {
                Property = Validation.HasErrorProperty,
                Value = true
            };
            trigger.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Red));
            trigger.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(3)));
            cellStyle.Triggers.Add(trigger);
            dataGrid.CellStyle = cellStyle;
        }

        private IEnumerable<PropertyInfo> GetDataGridProperties(Type listItemType)
        {
            var all = listItemType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            if (_dataGridAttribute?.ColumnMemberNames != null &&
                _dataGridAttribute.ColumnMemberNames.Length > 0)
            {
                var map = all.ToDictionary(p => p.Name, p => p);

                foreach (var name in _dataGridAttribute.ColumnMemberNames)
                {
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (map.TryGetValue(name, out var prop))
                        yield return prop;
                }

                yield break;
            }

            foreach (var p in all)
                yield return p;
        }

        private bool IsPropertyReadOnly(PropertyInfo propertyInfo, FlowBloxUIAttribute uiAttribute)
        {
            return _readOnly ||
                   !propertyInfo.CanWrite ||
                   uiAttribute?.ReadOnly == true;
        }

        public FrameworkElement Create()
        {
            if (!typeof(IList).IsAssignableFrom(_property.PropertyType))
                throw new InvalidOperationException("DataGridFactory can only work with IList-based properties.");

            var list = (IList)_property.GetValue(_target) ?? (IList)Activator.CreateInstance(_property.PropertyType);

            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = _readOnly || _uiAttribute?.ReadOnly == true,
                CanUserAddRows = false,
                CanUserDeleteRows = _associationAttribute?.Operations.HasFlag(UIOperations.Delete) ?? false,
                CanUserSortColumns = _dataGridAttribute?.IsMovable != true,
                SelectionMode = DataGridSelectionMode.Extended,
                ColumnWidth = DataGridLength.Auto
            };
            ParentScrollViewerMouseWheelForwarder.Register(dataGrid);
            SetCellStyle(dataGrid);

            var emptyMessage = CreateEmptyMessage(list);

            // Binding an die Liste setzen
            dataGrid.ItemsSource = list;

            // Set selection to preselectedInstance (if available)
            var preselectedInstance = ResolvePreselectedInstanceInCurrentTransaction(list);
            if (preselectedInstance != null && list.Contains(preselectedInstance))
            {
                dataGrid.SelectedItem = preselectedInstance;
                dataGrid.CurrentItem = preselectedInstance;
                dataGrid.Dispatcher.InvokeAsync(() =>
                {
                    dataGrid.ScrollIntoView(preselectedInstance);
                }, DispatcherPriority.ApplicationIdle);
            }

            // Spalten aus der Listenelement-Type erstellen
            Type listItemType = _property.PropertyType.GetGenericArguments()[0];
            foreach (var childProperty in GetDataGridProperties(listItemType))
            {
                var displayAttr = childProperty.GetCustomAttribute<DisplayAttribute>();
                if (displayAttr == null)
                    continue;

                var uiAttribute = childProperty.GetFlowBloxUIAttribute();
                var factoryType = childProperty.GetFlowBloxUIFactory();

                // Wenn die Grid-Columms automatisch ermittelt wurden, werden nicht sichtbare Member nicht als Spalten angezeigt:
                if (_dataGridAttribute?.ColumnMemberNames == null ||
                    _dataGridAttribute.ColumnMemberNames.Length == 0)
                {
                    if (uiAttribute?.Visible == false)
                        continue;
                }

                string headerText = FlowBloxResourceUtil.GetDisplayName(displayAttr);

                if (PropertyTypeHelper.IsSimpleType(childProperty.PropertyType) || factoryType == UIFactory.ComboBox)
                {
                    dataGrid.Columns.Add(CreateScalarColumn(headerText, childProperty, uiAttribute));
                    continue;
                }
                // Association (Create/Edit Button)
                if (factoryType == UIFactory.Association)
                {
                    var buttonColumn = new DataGridTemplateColumn
                    {
                        Header = headerText,
                        CellTemplate = new DataTemplate
                        {
                            VisualTree = CreateAssociationButtonTemplate(childProperty)
                        },
                        IsReadOnly = IsPropertyReadOnly(childProperty, uiAttribute)
                    };
                    dataGrid.Columns.Add(buttonColumn);
                }

            }

            // Commands für die Buttons
            _addCommand = new RelayCommand(() =>
            {
                AddNewItem(list, listItemType, dataGrid);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }, () => !_readOnly);

            _deleteCommand = new RelayCommand(async () =>
            {
                foreach (var item in dataGrid.SelectedItems.Cast<object>().ToList())
                {
                    if (await ConfirmDeletionAsync(item, _window))
                    {
                        DeleteInstance(item);
                        list.Remove(item);
                        FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                    }
                }
            }, () => dataGrid.SelectedItems.Count > 0 && !_readOnly);

            _editCommand = new RelayCommand(() =>
            {
                var item = dataGrid.SelectedItem;
                if (item != null)
                {
                    var success = WpfPropertyWindowProvider.CreatePropertyWindowAndShowDialog(_window, _target, item, _readOnly);
                    if (success)
                        FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                }
            }, () => dataGrid.SelectedItems.Count == 1 && _associationAttribute?.Operations.HasFlag(UIOperations.Edit) == true);

            dataGrid.MouseDoubleClick += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left || !_editCommand.CanExecute(null))
                    return;

                var source = dataGrid.InputHitTest(e.GetPosition(dataGrid)) as DependencyObject;
                while (source != null && source != dataGrid && source is not DataGridCell)
                {
                    if (source is System.Windows.Controls.Primitives.ButtonBase)
                        return;

                    source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
                }

                var cell = source as DataGridCell;
                if (cell == null || !cell.IsReadOnly)
                    return;

                var row = ItemsControl.ContainerFromElement(dataGrid, cell) as DataGridRow;
                if (row == null || !ReferenceEquals(row.Item, dataGrid.SelectedItem))
                    return;

                e.Handled = true;
                _editCommand.Execute(null);
            };

            _moveUpCommand = new RelayCommand(() =>
            {
                MoveItem(list, dataGrid, -1);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }, () => dataGrid.SelectedIndex > 0 && !_readOnly);

            _moveDownCommand = new RelayCommand(() =>
            {
                MoveItem(list, dataGrid, 1);
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            }, () => dataGrid.SelectedIndex >= 0 && dataGrid.SelectedIndex < list.Count - 1 && !_readOnly);

            // Toolbar for Create, Delete, MoveUp, MoveDown
            var toolBar = new ToolBar();

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Create) == true)
            {
                var addButton = CreateButton(PackIconMaterialKind.Plus, FlowBloxResourceUtil.GetLocalizedString("Buttons_Add", typeof(DataGridFactoryResources)), _addCommand, Brushes.Green);
                toolBar.Items.Add(addButton);
            }

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Edit) == true)
            {
                var editButton = CreateButton(PackIconMaterialKind.Pencil, FlowBloxResourceUtil.GetLocalizedString("Buttons_Edit", typeof(DataGridFactoryResources)), _editCommand, Brushes.Orange);
                toolBar.Items.Add(editButton);
            }

            if (_associationAttribute?.Operations.HasFlag(UIOperations.Delete) == true)
            {
                var removeButton = CreateButton(PackIconMaterialKind.Delete, FlowBloxResourceUtil.GetLocalizedString("Buttons_Remove", typeof(DataGridFactoryResources)), _deleteCommand, Brushes.DarkRed);
                toolBar.Items.Add(removeButton);
            }

            if (_dataGridAttribute?.IsMovable == true)
            {
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.ArrowUp, FlowBloxResourceUtil.GetLocalizedString("Buttons_MoveUp", typeof(DataGridFactoryResources)), _moveUpCommand, Brushes.Blue));
                toolBar.Items.Add(CreateButton(PackIconMaterialKind.ArrowDown, FlowBloxResourceUtil.GetLocalizedString("Buttons_MoveDown", typeof(DataGridFactoryResources)), _moveDownCommand, Brushes.Blue));
            }

            var container = new System.Windows.Controls.Grid();
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // DataGrid
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // EmptyMessage
            container.Children.Add(emptyMessage);
            container.Children.Add(dataGrid);

            System.Windows.Controls.Grid.SetRow(dataGrid, 0);
            System.Windows.Controls.Grid.SetRow(emptyMessage, 1);

            var stackPanel = new StackPanel();
            stackPanel.Children.Add(toolBar);
            stackPanel.Children.Add(container);

            // Event to activate/deactivate the buttons
            dataGrid.SelectionChanged += async (s, e) =>
            {
                _deleteCommand.Invalidate();
                _editCommand.Invalidate();
                _moveUpCommand.Invalidate();
                _moveDownCommand.Invalidate();
            };

            // Fire Property-Change Event when the cell value has changed
            dataGrid.CellEditEnding += (s, e) =>
            {
                FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
            };

            UpdateEmptyMessageVisibilityOnCollectionChanged(emptyMessage, list);

            return ResizableControlContainer.Create(stackPanel, minHeight: 150);
        }

        private DataGridColumn CreateScalarColumn(
            string header,
            PropertyInfo property,
            FlowBloxUIAttribute attribute)
        {
            var readOnly = IsPropertyReadOnly(property, attribute);
            var editor = new FrameworkElementFactory(typeof(ContentControl));
            editor.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
            editor.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            {
                var host = (ContentControl)sender;
                if (host.DataContext == null)
                    return;

                var resolver = new PropertyControlResolver(_window, _target, isGridCell: true);
                var control = resolver.CreateControl(
                    property, host.DataContext, header, attribute, readOnly, null).FrameworkElement;

                // Editors are also interactive outside DataGrid edit mode.
                // Their bindings notify the row, so relay those changes to the owning collection property.
                if (!readOnly && host.DataContext is INotifyPropertyChanged row)
                {
                    PropertyChangedEventHandler handler = (_, args) =>
                    {
                        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == property.Name)
                            FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                    };
                    row.PropertyChanged += handler;
                    control.Unloaded += (_, _) => row.PropertyChanged -= handler;
                }

                host.Content = control;
            }));
            var activation = property.GetCustomAttribute<ActivationConditionAttribute>();
            if (activation != null)
                return ActivationConditionAwareTextboxColumnFactory.Create(header, property, activation, readOnly, editor);

            var template = new DataTemplate { VisualTree = editor };
            return new DataGridTemplateColumn
            {
                Header = header,
                SortMemberPath = property.Name,
                MinWidth = DefaultTextColumnMinWidth,
                IsReadOnly = readOnly,
                CellTemplate = template,
                CellEditingTemplate = template
            };
        }
        private Button CreateButton(PackIconMaterialKind iconKind, string tooltip, RelayCommand command, Brush color)
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
                Margin = new Thickness(5)
            };
            ButtonStyleHelper.SetButtonStyle(button, color, Brushes.Gray);
            return button;
        }

        private FrameworkElementFactory CreateAssociationButtonTemplate(PropertyInfo property)
        {
            var readOnly = IsPropertyReadOnly(property, property.GetFlowBloxUIAttribute());
            var factory = new FrameworkElementFactory(typeof(Button));
            factory.SetValue(Button.ContentProperty, new Binding(property.Name)
            {
                Converter = new AssociationConverter()
            });
            factory.AddHandler(Button.ClickEvent, new RoutedEventHandler((s, e) =>
            {
                if (s is Button button && button.DataContext != null)
                {
                    var dataContext = button.DataContext;

                    var item = property.GetValue(dataContext, null);
                    var isNew = false;
                    if (item == null)
                    {
                        if (readOnly)
                        {
                            FlowBloxServiceLocator.Instance.GetService<IFlowBloxMessageBoxService>()
                                ?.ShowMessageBox(
                                    FlowBloxResourceUtil.GetLocalizedString("ObjectCreationReadOnlyMessage", typeof(DataGridFactoryResources)),
                                    FlowBloxResourceUtil.GetLocalizedString("ObjectCreationReadOnlyTitle", typeof(DataGridFactoryResources)),
                                    FlowBloxMessageBoxTypes.Information);
                            return;
                        }

                        var newInstance = CreateNewInstance(_window, property.PropertyType);
                        if (newInstance == null)
                            return;

                        property.SetValue(dataContext, newInstance);
                        FlowBloxComponentHelper.RaisePropertyChanged(dataContext, property.Name);
                        item = newInstance;
                        isNew = true;
                    }
                    var propertyView = new Views.PropertyWindow(new PropertyWindowArgs(item, parent: _target, readOnly: readOnly, isNew: isNew));
                    propertyView.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                    propertyView.Owner = _window;
                    if (propertyView.ShowDialog() == true)
                    {
                        FlowBloxComponentHelper.RaisePropertyChanged(dataContext, property.Name);
                        FlowBloxComponentHelper.RaisePropertyChanged(_target, _property.Name);
                    }
                }
            }));
            return factory;
        }

        protected override Type GetExplicitItemFactoryType(Type type)
        {
            var itemType = _property.PropertyType.GetGenericArguments()[0];
            return itemType.IsAssignableFrom(type) ? _associationAttribute.ItemFactory : null;
        }

        private void AddNewItem(IList list, Type listItemType, DataGrid dataGrid)
        {
            object newItem = CreateNewInstance(_window, listItemType);
            if (newItem == null)
                return;

            list.Add(newItem);

            if (_property.GetValue(_target) == null)
                _property.SetValue(_target, list);

            dataGrid.SelectedItems.Clear();
            dataGrid.SelectedItem = newItem;

            dataGrid.Dispatcher.InvokeAsync(() =>
            {
                if (!dataGrid.IsLoaded || !ReferenceEquals(dataGrid.SelectedItem, newItem))
                    return;

                var firstEditableColumn = dataGrid.Columns
                    .Where(column => column.Visibility == Visibility.Visible && !column.IsReadOnly)
                    .OrderBy(column => column.DisplayIndex)
                    .FirstOrDefault();

                if (dataGrid.IsReadOnly || firstEditableColumn == null)
                {
                    dataGrid.ScrollIntoView(newItem);
                    dataGrid.Focus();
                    return;
                }

                dataGrid.ScrollIntoView(newItem, firstEditableColumn);
                dataGrid.CurrentCell = new DataGridCellInfo(newItem, firstEditableColumn);
                dataGrid.Focus();
                dataGrid.UpdateLayout();

                var content = firstEditableColumn.GetCellContent(newItem);
                if (content != null)
                {
                    DependencyObject element = content;
                    while (element != null && element is not DataGridCell)
                        element = VisualTreeHelper.GetParent(element);

                    (element as DataGridCell)?.Focus();
                }

                dataGrid.BeginEdit();
            }, DispatcherPriority.Input);
        }

        private void MoveItem(IList list, DataGrid dataGrid, int direction)
        {
            int index = dataGrid.SelectedIndex;
            if (index < 0 || (index == 0 && direction == -1) || (index == list.Count - 1 && direction == 1)) return;
            var item = list[index];
            list.RemoveAt(index);
            list.Insert(index + direction, item);
            dataGrid.SelectedIndex = index + direction;
        }
    }
}
