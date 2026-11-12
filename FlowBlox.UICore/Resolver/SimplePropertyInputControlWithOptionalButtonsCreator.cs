using SimplePropertyInputControlWithOptionalButtonsCreatorResources = FlowBlox.UICore.Resources.ClassResources.SimplePropertyInputControlWithOptionalButtonsCreator;
using FlowBlox.Core;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Models.Base;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Converters.PropertyView;
using FlowBlox.UICore.Enums;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;
using MahApps.Metro.IconPacks;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FlowBlox.UICore.Resolver
{
    public class SimplePropertyInputControlWithOptionalButtonsCreator
    {
        private const double IconSize = 10;
        private readonly Window _window;
        private readonly object _parent;
        private readonly bool _isGridCell;

        public SimplePropertyInputControlWithOptionalButtonsCreator(Window window, object parent = null, bool isGridCell = false)
        {
            _window = window;
            _parent = parent;
            _isGridCell = isGridCell;
        }

        public FrameworkElement CreateSimplePropertyInputControlWithOptionalButtons(
            PropertyInfo property,
            object target,
            FlowBloxUIAttribute uiAttribute,
            Binding binding,
            bool readOnly,
            FrameworkElement inputControl = null)
        {
            var hasFieldSelection = FlowBloxFieldSelection.SupportsScalar(property.PropertyType)
                && FlowBloxFieldSelection.Supports(property)
                && target is FlowBloxReactiveObject;
            inputControl ??= CreateInputControl(property, target, binding, readOnly);

            if (!hasFieldSelection && EnumHelper.IsEnumType(property))
            {
                var container = new Border { Child = inputControl };
                container.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.EditorContainerStyle");
                return container;
            }

            var grid = new System.Windows.Controls.Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var contentHost = new System.Windows.Controls.Grid();
            contentHost.Children.Add(inputControl);

            if (hasFieldSelection)
            {
                var selectedFieldPanel = CreateSelectedFieldPanel(property, target);
                contentHost.Children.Add(selectedFieldPanel);

                inputControl.SetBinding(
                    UIElement.VisibilityProperty,
                    CreateSelectedFieldVisibilityBinding(target, property.Name, visibleWhenNull: true));

                selectedFieldPanel.SetBinding(
                    UIElement.VisibilityProperty,
                    CreateSelectedFieldVisibilityBinding(target, property.Name, visibleWhenNull: false));
            }

            System.Windows.Controls.Grid.SetColumn(contentHost, 0);
            grid.Children.Add(contentHost);

            var columnIndex = 1;
            if (hasFieldSelection)
            {
                var selectButton = CreateSelectFieldButton(property, (FlowBloxReactiveObject)target, readOnly);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(selectButton, columnIndex++);
                grid.Children.Add(selectButton);

                var unlinkButton = CreateUnlinkButton(property, (FlowBloxReactiveObject)target, readOnly);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(unlinkButton, columnIndex);
                grid.Children.Add(unlinkButton);
            }

            return grid;
        }

        private FrameworkElement CreateInputControl(PropertyInfo property, object target, Binding binding, bool readOnly)
        {
            if (EnumHelper.IsEnumType(property))
            {
                var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var enumDropDown = new EnumerationDropDownControl
                {
                    ItemsSource = EnumerationDropDown.CreateItems(enumType, Nullable.GetUnderlyingType(property.PropertyType) != null),
                    IsHitTestVisible = !readOnly,
                    Focusable = !readOnly,
                    IsTabStop = !readOnly
                };
                PropertyEditorBinding.Bind(enumDropDown, EnumerationDropDownControl.SelectedValueProperty, binding, target, property.Name);
                return enumDropDown;
            }

            if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
            {
                var toggle = new ToggleSwitch
                {
                    IsEnabled = !readOnly,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = _isGridCell ? new Thickness(0) : new Thickness(0, 4, 0, 4),
                    MinWidth = 80,
                    OnContent = FlowBloxResourceUtil.GetLocalizedString("ToggleSwitch_OnContent", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources)),
                    OffContent = FlowBloxResourceUtil.GetLocalizedString("ToggleSwitch_OffContent", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources))
                };

                if (_isGridCell)
                {
                    toggle.MinHeight = 24;
                    toggle.Padding = new Thickness(0);
                }
                PropertyEditorBinding.Bind(toggle, ToggleSwitch.IsOnProperty, binding, target, property.Name);
                return toggle;
            }

            if (property.PropertyType == typeof(int) ||
                property.PropertyType == typeof(int?) ||
                property.PropertyType == typeof(long) ||
                property.PropertyType == typeof(long?) ||
                property.PropertyType == typeof(float) ||
                property.PropertyType == typeof(float?) ||
                property.PropertyType == typeof(double) ||
                property.PropertyType == typeof(double?))
            {
                binding.Converter = new NumericTextValueConverter(property.PropertyType);

                var textBox = new TextBox
                {
                    IsReadOnly = readOnly
                };
                textBox.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.TextEditorStyle");
                PropertyEditorBinding.Bind(textBox, TextBox.TextProperty, binding, target, property.Name);
                return textBox;
            }

            if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            {
                var datePicker = new DatePicker
                {
                    IsEnabled = !readOnly
                };
                PropertyEditorBinding.Bind(datePicker, DatePicker.SelectedDateProperty, binding, target, property.Name);
                return datePicker;
            }

            throw new InvalidOperationException($"Unsupported simple property type '{property.PropertyType.Name}'.");
        }

        private FrameworkElement CreateSelectedFieldPanel(PropertyInfo property, object target)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new PackIconMaterial
            {
                Kind = PackIconMaterialKind.Variable,
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(FlowBloxReactiveObject.PropertySelectedFieldMap))
            {
                Source = target,
                Converter = new SelectedFieldNameConverter(),
                ConverterParameter = property.Name
            });

            panel.Children.Add(icon);
            panel.Children.Add(text);

            return panel;
        }

        private Button CreateSelectFieldButton(
            PropertyInfo property,
            FlowBloxReactiveObject target,
            bool readOnly)
        {
            var fieldSelectionAttribute = property.GetCustomAttribute<FlowBloxFieldSelectionAttribute>();
            var button = new Button
            {
                Content = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.Variable,
                    Width = IconSize,
                    Height = IconSize
                },
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = FlowBloxResourceUtil.GetLocalizedString("EnableFieldSelection_Tooltip", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources)),
                IsEnabled = !readOnly
            };

            ((PackIconMaterial)button.Content).SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.FieldSelectionIconStyle");
            button.Command = new RelayCommand(async () =>
            {
                try
                {
                    var args = Utilities.TextBoxHelper.CreateFieldSelectionWindowArgs(target, fieldSelectionAttribute, _parent);
                    args.SelectionMode = FieldSelectionMode.Fields;
                    args.MultiSelect = false;
                    var result = Utilities.TextBoxHelper.ShowFieldSelectionDialog(args, _window);
                    var selectedField = result?.SelectedFields?.FirstOrDefault();
                    if (selectedField == null)
                        return;

                    target.SetPropertySelectedField(property.Name, selectedField);
                    FlowBlockHelper.ApplyFieldSelectionRequiredOption(target, [selectedField], result.IsRequired);
                }
                catch (Exception ex)
                {
                    await MessageBoxHelper.ShowMessageBoxAsync(_window as MetroWindow, MessageBoxType.Error, ex.Message);
                }
            });

            return button;
        }

        private Button CreateUnlinkButton(
            PropertyInfo property,
            FlowBloxReactiveObject target,
            bool readOnly)
        {
            var button = new Button
            {
                Content = new PackIconMaterial
                {
                    Kind = PackIconMaterialKind.LinkOff,
                    Width = IconSize,
                    Height = IconSize
                },
                Width = 24,
                Height = 24,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(6, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = FlowBloxResourceUtil.GetLocalizedString("Unlink", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources)),
                IsEnabled = !readOnly
            };

            ((PackIconMaterial)button.Content).SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.UnlinkIconStyle");
            if (!readOnly)
            {
                button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(FlowBloxReactiveObject.PropertySelectedFieldMap))
                {
                    Source = target,
                    Converter = new NullToBoolConverter(trueWhenNull: false),
                    ConverterParameter = property.Name
                });
            }

            button.Command = new RelayCommand(() =>
            {
                if (readOnly)
                    return;

                target.SetPropertySelectedField(property.Name, null);
            });

            return button;
        }

        private static string GetSelectedFieldName(object value, object parameter)
        {
            var selections = value as IReadOnlyDictionary<string, string>;
            return selections != null && selections.TryGetValue((string)parameter, out var fieldName)
                ? fieldName
                : null;
        }

        private static Binding CreateSelectedFieldVisibilityBinding(object target, string selectedFieldPropertyName, bool visibleWhenNull)
        {
            return new Binding(nameof(FlowBloxReactiveObject.PropertySelectedFieldMap))
            {
                Source = target,
                Converter = new NullToVisibilityConverter(visibleWhenNull),
                ConverterParameter = selectedFieldPropertyName
            };
        }

        private sealed class SelectedFieldNameConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                return GetSelectedFieldName(value, parameter);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class NullToVisibilityConverter : IValueConverter
        {
            private readonly bool _visibleWhenNull;

            public NullToVisibilityConverter(bool visibleWhenNull)
            {
                _visibleWhenNull = visibleWhenNull;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var isNull = string.IsNullOrWhiteSpace(GetSelectedFieldName(value, parameter));
                return isNull == _visibleWhenNull ? Visibility.Visible : Visibility.Collapsed;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }

        private sealed class NullToBoolConverter : IValueConverter
        {
            private readonly bool _trueWhenNull;

            public NullToBoolConverter(bool trueWhenNull)
            {
                _trueWhenNull = trueWhenNull;
            }

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var isNull = string.IsNullOrWhiteSpace(GetSelectedFieldName(value, parameter));
                return isNull == _trueWhenNull;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
