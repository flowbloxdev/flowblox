using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Utilities;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckEditorPopupsAsync(string directory, Action<bool, string> check)
    {
        var window = new Window
        {
            Width = 420,
            Height = 180,
            ShowInTaskbar = false,
            ShowActivated = false
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FlowBlox.UICore;component/Views/Styles/HostedControlStyles.xaml", UriKind.Relative)
        });
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/FlowBlox.UICore;component/Views/Styles/PropertyViewStyles.xaml", UriKind.Relative)
        });
        var model = new WebRequestFlowBlock();
        var property = typeof(WebRequestFlowBlock).GetProperty(nameof(WebRequestFlowBlock.ResponseBodyKind));
        var editor = new PropertyControlResolver(window).Resolve(
            property,
            model,
            false,
            null,
            new PropertyControlBindingContext(model)).Control;
        var textBox = new TextBox
        {
            Text = "Example property value",
            Margin = new Thickness(0, 12, 0, 0),
            Style = (Style)window.FindResource("PropertyView.TextEditorStyle")
        };
        var content = new StackPanel { Margin = new Thickness(16) };
        content.Children.Add(editor);
        content.Children.Add(textBox);
        window.Content = content;

        try
        {
            window.Show();
            window.UpdateLayout();
            var dropdown = MenuVisuals<EnumerationDropDownControl>(window).Single();
            dropdown.IsChecked = true;
            await Task.Delay(100);
            var menu = dropdown.DropDownMenu;
            menu.UpdateLayout();
            var surface = menu.Template.FindName("SelectionSurface", menu) as Border;
            check(surface != null && menu.ActualWidth >= dropdown.ActualWidth,
                "Generated enum selector opens a compact control-width popup");
            var controlEdge = dropdown.PointToScreen(new Point(0, dropdown.ActualHeight));
            var popupEdge = menu.PointToScreen(new Point());
            check(Math.Abs(controlEdge.X - popupEdge.X) <= 1 && Math.Abs(controlEdge.Y - popupEdge.Y) <= 1,
                "Enum dropdown attaches directly to its control without a floating-menu margin");
            SaveMenuImage(directory, menu, "enum-selection-dropdown.png");
            var choice = dropdown.ItemsSource.First(item => item.Value != null && !Equals(item.Value, dropdown.SelectedValue));
            var row = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, choice.Display));
            var peer = new MenuItemAutomationPeer(row);
            ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
            await Task.Delay(100);
            check(Equals(model.ResponseBodyKind, choice.Value) && !menu.IsOpen,
                "Styled enum selection updates the property binding and dismisses the dropdown");

            textBox.SelectAll();
            var textMenu = textBox.ContextMenu;
            textMenu.PlacementTarget = textBox;
            textMenu.IsOpen = true;
            await Task.Delay(100);
            textMenu.UpdateLayout();
            check(textMenu.Template.FindName("MenuSurface", textMenu) is Border,
                "Property text editor uses the shared context-menu surface");
            var cut = textMenu.Items.OfType<MenuItem>().Single(item => item.Command == ApplicationCommands.Cut);
            var copy = textMenu.Items.OfType<MenuItem>().Single(item => item.Command == ApplicationCommands.Copy);
            check(ReferenceEquals(cut.CommandTarget, textBox) && cut.IsEnabled && copy.IsEnabled,
                "Text menu commands target the selected property editor");
            SaveMenuImage(directory, textMenu, "property-text-context-menu.png");
            textBox.IsReadOnly = true;
            CommandManager.InvalidateRequerySuggested();
            await Task.Delay(100);
            check(!cut.IsEnabled && copy.IsEnabled,
                "Read-only property text keeps Copy available and disables Cut");
            textMenu.IsOpen = false;
        }
        finally
        {
            window.Close();
        }
    }
}
