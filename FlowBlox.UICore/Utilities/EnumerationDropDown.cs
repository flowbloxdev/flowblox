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
    /// <summary>
    /// Shared enum items and optional submenu support for property and grid editors.
    /// </summary>
    public static class EnumerationDropDown
    {
        public sealed class Item : SelectionDropDown.Item
        {
            public Item(string display, Enum? value, string? group, UIIconDefinition groupIcon = default)
                : base(display, value, group, groupIcon)
            {
            }
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
            => SelectionDropDown.CreateMenu(control, items);
    }
}
