using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.Additions;
using FlowBlox.UICore.Utilities;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Input;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Factory.PropertyView;

namespace FlowBloxTest.Views;

[TestClass]
[TestCategory(FlowBloxTestCategories.UnitTest)]
public class EnumerationDropDownTests
{
    [STATestMethod]
    [DataRow("en", "Advanced text operators")]
    [DataRow("de", "Erweiterte Textoperatoren")]
    public void Menu_GroupsAdvancedOperators_AndPreservesTwoWayBinding(string culture, string groupName)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var items = EnumerationDropDown.CreateItems(typeof(ComparisonOperator), false);
            var condition = new ComparisonCondition { Operator = ComparisonOperator.Equals };
            var dropdown = new EnumerationDropDownControl
            {
                ItemsSource = items,
                FontSize = 12
            };
            dropdown.SetBinding(EnumerationDropDownControl.SelectedValueProperty, new Binding(nameof(ComparisonCondition.Operator))
            {
                Source = condition, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
            var menu = EnumerationDropDown.CreateMenu(dropdown, items);
            var group = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, groupName));
            Assert.AreEqual(6, group.Items.Count);
            Assert.AreEqual(11, menu.Items.Count);
            var icon = group.Icon as Image;
            Assert.IsNotNull(icon);
            Assert.AreEqual(16d, icon.Width);
            Assert.AreEqual(16d, icon.Height);
            Assert.IsNotNull(icon.Source);
            Assert.AreEqual(dropdown.FontSize, menu.FontSize);
            foreach (var entry in menu.Items.OfType<MenuItem>().Concat(group.Items.OfType<MenuItem>()))
            {
                Assert.AreEqual(dropdown.FontSize, entry.FontSize);
                Assert.AreEqual(dropdown.FontFamily, entry.FontFamily);
                Assert.AreEqual(2d, entry.Padding.Top);
                Assert.AreEqual(2d, entry.Padding.Bottom);
            }

            var selected = items.Single(item => Equals(item.Value, ComparisonOperator.XPathIsFalse));
            var choice = group.Items.OfType<MenuItem>().Single(item => Equals(item.Header, selected.Display));
            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.AreEqual(ComparisonOperator.XPathIsFalse, condition.Operator);
            Assert.IsTrue(BindingOperations.IsDataBound(dropdown, EnumerationDropDownControl.SelectedValueProperty));

            condition.Operator = ComparisonOperator.JPathIsTrue;
            Assert.AreEqual(ComparisonOperator.JPathIsTrue, dropdown.SelectedValue);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
        }
    }

    [STATestMethod]
    public void NullableEnums_CanClearSelection_AndUngroupedEnumsStayFlat()
    {
        var items = EnumerationDropDown.CreateItems(typeof(DayOfWeek), true);
        Assert.AreEqual(8, items.Count);
        Assert.IsNull(items[0].Value);
        Assert.IsTrue(items.All(item => item.Group == null));
        var dropdown = new EnumerationDropDownControl { ItemsSource = items };
        dropdown.SelectedValue = DayOfWeek.Monday;
        var menu = EnumerationDropDown.CreateMenu(dropdown, items);
        Assert.IsTrue(menu.Items.OfType<MenuItem>().All(item => item.Icon == null));
        ((MenuItem)menu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.IsNull(dropdown.SelectedValue);
    }

    public class ConditionList
    {
        public List<ComparisonCondition> Conditions { get; } = [new()];
    }

    [STATestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void PropertyAndGridEditors_OpenSubmenu_AndCommitSelection(bool useGrid, bool useKeyboard)
    {
        var window = new Window { Width = 500, Height = 300, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FlowBlox.UICore;component/Views/MahAppMetroResources.xaml", UriKind.Relative)
        });
        ContextMenu? menu = null;
        try
        {
            var condition = new ComparisonCondition();
            DataGrid? grid = null;
            if (useGrid)
            {
                var list = new ConditionList();
                condition = list.Conditions[0];
                window.Content = new DataGridFactory(window, typeof(ConditionList).GetProperty(nameof(ConditionList.Conditions))!, list, false).Create();
            }
            else
            {
                var property = typeof(ComparisonCondition).GetProperty(nameof(ComparisonCondition.Operator))!;
                window.Content = new PropertyControlResolver(window).Resolve(property, condition, false, null,
                    new PropertyControlBindingContext(condition)).Control;
            }
            window.Show();
            window.UpdateLayout();
            if (useGrid)
            {
                grid = Descendants(window).OfType<DataGrid>().Single();
                var column = grid.Columns.OfType<DataGridTemplateColumn>().First();
                grid.CurrentCell = new DataGridCellInfo(condition, column);
                Assert.IsTrue(grid.BeginEdit());
                window.UpdateLayout();
            }

            var dropdown = Descendants(window).OfType<EnumerationDropDownControl>().Single();
            if (useKeyboard)
                dropdown.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.F4)
                {
                    RoutedEvent = Keyboard.KeyDownEvent
                });
            else
                dropdown.IsChecked = true;
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Assert.IsTrue(dropdown.IsChecked);
            menu = PresentationSource.CurrentSources.Cast<PresentationSource>()
                .Where(source => source.Dispatcher.CheckAccess())
                .SelectMany(source => source.RootVisual == null ? [] : Descendants(source.RootVisual))
                .OfType<ContextMenu>().Single(value => value.IsOpen && value.PlacementTarget == dropdown);
            var group = menu.Items.OfType<MenuItem>().Single(item => item.HasItems);
            Assert.AreEqual(6, group.Items.Count);
            menu.UpdateLayout();
            foreach (var entry in menu.Items.OfType<MenuItem>())
            {
                Assert.AreEqual(dropdown.FontSize, entry.FontSize);
                Assert.IsTrue(entry.ActualHeight <= dropdown.FontSize * 1.5 + 6,
                    $"Dropdown row is too tall: {entry.ActualHeight} for font size {dropdown.FontSize}.");
            }
            var selectedItem = ((IEnumerable<EnumerationDropDown.Item>)dropdown.ItemsSource)
                .Single(item => Equals(item.Value, ComparisonOperator.JPathIsFalse));
            var choice = group.Items.OfType<MenuItem>().Single(item => Equals(item.Header, selectedItem.Display));
            choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.IsFalse(dropdown.IsChecked);
            Assert.IsFalse(menu.IsOpen);
            if (grid != null)
            {
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Cell, true));
                Assert.IsTrue(grid.CommitEdit(DataGridEditingUnit.Row, true));
            }
            Assert.AreEqual(ComparisonOperator.JPathIsFalse, condition.Operator);
        }
        finally
        {
            if (menu != null) menu.IsOpen = false;
            window.Close();
        }
    }

    [STATestMethod]
    public void ReadOnlyProperty_CannotOpenMenu()
    {
        var window = new Window();
        var condition = new ComparisonCondition();
        var property = typeof(ComparisonCondition).GetProperty(nameof(ComparisonCondition.Operator))!;
        var editor = new PropertyControlResolver(window).Resolve(property, condition, true, null,
            new PropertyControlBindingContext(condition)).Control;
        var dropdown = (EnumerationDropDownControl)((Border)editor).Child;
        dropdown.IsChecked = true;
        Assert.IsFalse(dropdown.IsChecked);
        Assert.IsNull(dropdown.DropDownMenu);
        Assert.IsFalse(dropdown.Focusable);
        window.Close();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
