using SimplePropertyInputControlWithOptionalButtonsCreatorResources = FlowBlox.UICore.Resources.ClassResources.SimplePropertyInputControlWithOptionalButtonsCreator;
using FlowBlox.Core;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Constants;
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
        private readonly FlowBloxComponentIcon16Converter _iconConverter = new();

        public SimplePropertyInputControlWithOptionalButtonsCreator(Window window)
        {
            _window = window;
        }

        public FrameworkElement CreateSimplePropertyInputControlWithOptionalButtons(
            PropertyInfo property,
            object target,
            FlowBloxUIAttribute uiAttribute,
            Binding binding,
            bool readOnly)
        {
            var hasFieldSelection = uiAttribute?.UiOptions.HasFlag(UIOptions.EnableFieldSelection) == true;
            var selectedFieldProperty = ResolveSelectedFieldProperty(target, property);
            var inputControl = CreateInputControl(property, target, binding, readOnly);

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

            if (hasFieldSelection && selectedFieldProperty != null)
            {
                var selectedFieldPanel = CreateSelectedFieldPanel(selectedFieldProperty, target);
                contentHost.Children.Add(selectedFieldPanel);

                inputControl.SetBinding(
                    UIElement.VisibilityProperty,
                    CreateSelectedFieldVisibilityBinding(target, selectedFieldProperty.Name, visibleWhenNull: true));

                selectedFieldPanel.SetBinding(
                    UIElement.VisibilityProperty,
                    CreateSelectedFieldVisibilityBinding(target, selectedFieldProperty.Name, visibleWhenNull: false));
            }

            System.Windows.Controls.Grid.SetColumn(contentHost, 0);
            grid.Children.Add(contentHost);

            var columnIndex = 1;
            if (hasFieldSelection)
            {
                var selectButton = CreateSelectFieldButton(property, target, selectedFieldProperty, readOnly);
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                System.Windows.Controls.Grid.SetColumn(selectButton, columnIndex++);
                grid.Children.Add(selectButton);

                var unlinkButton = CreateUnlinkButton(property, target, selectedFieldProperty, readOnly);
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
                enumDropDown.SelectionChanged += (s, e) => FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
                enumDropDown.SetBinding(EnumerationDropDownControl.SelectedValueProperty, binding);
                return enumDropDown;
            }

            if (property.PropertyType == typeof(bool) || property.PropertyType == typeof(bool?))
            {
                var toggle = new ToggleSwitch
                {
                    IsEnabled = !readOnly,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 4),
                    MinWidth = 80,
                    OnContent = FlowBloxResourceUtil.GetLocalizedString("ToggleSwitch_OnContent", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources)),
                    OffContent = FlowBloxResourceUtil.GetLocalizedString("ToggleSwitch_OffContent", typeof(SimplePropertyInputControlWithOptionalButtonsCreatorResources))
                };

                toggle.SetBinding(ToggleSwitch.IsOnProperty, binding);
                toggle.Toggled += (s, e) => FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
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
                textBox.TextChanged += (s, e) => FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
                textBox.SetResourceReference(FrameworkElement.StyleProperty, "PropertyView.TextEditorStyle");
                textBox.SetBinding(TextBox.TextProperty, binding);
                return textBox;
            }

            if (property.PropertyType == typeof(DateTime) || property.PropertyType == typeof(DateTime?))
            {
                var datePicker = new DatePicker
                {
                    IsEnabled = !readOnly
                };
                datePicker.SetBinding(DatePicker.SelectedDateProperty, binding);
                datePicker.SelectedDateChanged += (s, e) => FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
                return datePicker;
            }

            throw new InvalidOperationException($"Unsupported simple property type '{property.PropertyType.Name}'.");
        }

        private FrameworkElement CreateSelectedFieldPanel(PropertyInfo selectedFieldProperty, object target)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var icon = new Image
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            icon.SetBinding(Image.SourceProperty, new Binding(selectedFieldProperty.Name)
            {
                Source = target,
                Converter = _iconConverter
            });

            var text = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            text.SetBinding(TextBlock.TextProperty, new Binding($"{selectedFieldProperty.Name}.FullyQualifiedName")
            {
                Source = target
            });

            panel.Children.Add(icon);
            panel.Children.Add(text);

            return panel;
        }

        private Button CreateSelectFieldButton(
            PropertyInfo property,
            object target,
            PropertyInfo selectedFieldProperty,
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
                    if (selectedFieldProperty == null)
                        throw new InvalidOperationException(
                            $"Selected field property '{property.Name}{GlobalConstants.SimplePropertySelectedFieldSuffix}' was not found on '{target.GetType().Name}'.");

                    var args = Utilities.TextBoxHelper.CreateFieldSelectionWindowArgs(target, fieldSelectionAttribute);
                    args.SelectionMode = FieldSelectionMode.Fields;
                    args.MultiSelect = false;
                    var result = Utilities.TextBoxHelper.ShowFieldSelectionDialog(args, _window);
                    var selectedField = result?.SelectedFields?.FirstOrDefault();
                    if (selectedField == null)
                        return;

                    selectedFieldProperty.SetValue(target, selectedField);
                    FlowBlockHelper.ApplyFieldSelectionRequiredOption(target, [selectedField], result.IsRequired);
                    FlowBloxComponentHelper.RaisePropertyChanged(target, selectedFieldProperty.Name);
                    FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
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
            object target,
            PropertyInfo selectedFieldProperty,
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
            if (!readOnly && selectedFieldProperty != null)
            {
                button.SetBinding(UIElement.IsEnabledProperty, new Binding(selectedFieldProperty.Name)
                {
                    Source = target,
                    Converter = new NullToBoolConverter(trueWhenNull: false)
                });
            }

            button.Command = new RelayCommand(() =>
            {
                if (readOnly || selectedFieldProperty == null)
                    return;

                selectedFieldProperty.SetValue(target, null);
                FlowBloxComponentHelper.RaisePropertyChanged(target, selectedFieldProperty.Name);
                FlowBloxComponentHelper.RaisePropertyChanged(target, property.Name);
            });

            return button;
        }

        private static PropertyInfo ResolveSelectedFieldProperty(object target, PropertyInfo property)
        {
            return target?.GetType()?.GetProperty(property.Name + GlobalConstants.SimplePropertySelectedFieldSuffix);
        }

        private static Binding CreateSelectedFieldVisibilityBinding(object target, string selectedFieldPropertyName, bool visibleWhenNull)
        {
            return new Binding(selectedFieldPropertyName)
            {
                Source = target,
                Converter = new NullToVisibilityConverter(visibleWhenNull)
            };
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
                var isNull = value == null;
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
                var isNull = value == null;
                return isNull == _trueWhenNull;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
