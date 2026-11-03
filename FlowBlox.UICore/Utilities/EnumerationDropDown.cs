using FlowBlox.Core.Attributes;
using FlowBlox.Core.Extensions;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using FlowBlox.UICore.Converters;

namespace FlowBlox.UICore.Utilities
{
    /// <summary>Shared enum items and optional submenu support for property and grid editors.</summary>
    public static class EnumerationDropDown
    {
        public sealed class Item
        {
            public Item(string display, Enum? value, string? group, UIIconDefinition groupIcon = default)
            {
                Display = display;
                Value = value;
                Group = group;
                GroupIcon = groupIcon;
            }

            public string Display { get; }
            public Enum? Value { get; }
            public string? Group { get; }
            public UIIconDefinition GroupIcon { get; }
        }

        public static IReadOnlyList<Item> CreateItems(Type enumType, bool nullable)
        {
            var items = Enum.GetValues(enumType).Cast<Enum>()
                .Select(value =>
                {
                    var group = enumType.GetField(value.ToString())?
                        .GetCustomAttribute<FlowBloxEnumerationGroupAttribute>();
                    return new Item(value.GetDisplayName(), value, group?.GetDisplayName(), group?.IconDefinition ?? default);
                })
                .ToList();
            if (nullable)
                items.Insert(0, new Item(string.Empty, null, null));
            return items;
        }

        public static DataGridColumn CreateColumn(string header, IReadOnlyList<Item> items, string propertyName, bool readOnly)
        {
            var display = new FrameworkElementFactory(typeof(TextBlock));
            display.SetBinding(TextBlock.TextProperty, new Binding(propertyName) { Converter = new EnumDisplayNameConverter() });
            display.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var editor = new FrameworkElementFactory(typeof(EnumerationDropDownControl));
            editor.SetValue(EnumerationDropDownControl.ItemsSourceProperty, items);
            editor.SetBinding(EnumerationDropDownControl.SelectedValueProperty, new Binding(propertyName)
            {
                Mode = readOnly ? BindingMode.OneWay : BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            return new DataGridTemplateColumn
            {
                Header = header,
                IsReadOnly = readOnly,
                CellTemplate = new DataTemplate { VisualTree = display },
                CellEditingTemplate = new DataTemplate { VisualTree = editor }
            };
        }

        public static ContextMenu CreateMenu(EnumerationDropDownControl control, IReadOnlyList<Item> items)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = control,
                Placement = PlacementMode.Bottom,
                MinWidth = control.ActualWidth,
                FontSize = control.FontSize,
                FontFamily = control.FontFamily,
                FontWeight = control.FontWeight,
                FontStyle = control.FontStyle,
                Padding = new Thickness(0, 1, 0, 1),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            // Apply to both levels: popup menus otherwise use their own theme font and row spacing.
            var itemStyle = new Style(typeof(MenuItem), control.TryFindResource(typeof(MenuItem)) as Style);
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, control.FontSize));
            itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, control.FontFamily));
            itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, control.FontWeight));
            itemStyle.Setters.Add(new Setter(Control.FontStyleProperty, control.FontStyle));
            itemStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
            var groups = new Dictionary<string, MenuItem>();
            foreach (var item in items)
            {
                var choice = new MenuItem
                {
                    Header = string.IsNullOrEmpty(item.Display) ? "\u00a0" : item.Display,
                    IsCheckable = true,
                    Style = itemStyle,
                    // A local value also overrides the theme's MenuItem.Role padding triggers.
                    Padding = new Thickness(4, 2, 4, 2),
                    IsChecked = Equals(control.SelectedValue, item.Value)
                };
                choice.Click += (_, args) =>
                {
                    control.SetCurrentValue(EnumerationDropDownControl.SelectedValueProperty, item.Value);
                    menu.IsOpen = false;
                    control.SetCurrentValue(ToggleButton.IsCheckedProperty, false);
                    control.Focus();
                    args.Handled = true;
                };
                if (item.Group == null)
                {
                    menu.Items.Add(choice);
                    continue;
                }

                if (!groups.TryGetValue(item.Group, out var group))
                {
                    group = new MenuItem { Header = item.Group, Style = itemStyle, Padding = new Thickness(4, 2, 4, 2) };
                    if (item.GroupIcon.TryCreateSKImage(out var icon))
                    {
                        group.Icon = new Image
                        {
                            Source = SkiaWpfImageHelper.ConvertToImageSource(icon),
                            Width = 16,
                            Height = 16,
                            SnapsToDevicePixels = true
                        };
                    }
                    groups.Add(item.Group, group);
                    menu.Items.Add(group);
                }
                group.Items.Add(choice);
            }
            return menu;
        }
    }
}
