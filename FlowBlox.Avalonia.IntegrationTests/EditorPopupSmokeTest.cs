using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
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
            for (var attempt = 0; attempt < 40 && (!Equals(model.ResponseBodyKind, choice.Value) || menu.IsOpen); attempt++)
                await Task.Delay(50);
            check(Equals(model.ResponseBodyKind, choice.Value) && !menu.IsOpen,
                $"Styled enum selection updates the property binding and dismisses the dropdown (expected: {choice.Value}, model: {model.ResponseBodyKind}, selected: {dropdown.SelectedValue}, open: {menu.IsOpen})");

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
            await CheckFieldEditorChangesAsync(window, content, directory, check);
        }
        finally
        {
            window.Close();
        }
    }

    private async Task CheckFieldEditorChangesAsync(Window window, StackPanel content, string directory, Action<bool, string> check)
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            Name = "Input_EditorCheck",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            IsRequiredAtRuntimeStartup = true,
            StringValue = "",
            ListOfValues = [new ValueItem { DisplayName = "Example", Value = "example" }]
        };
        var context = new PropertyControlBindingContext(field);
        var resolver = new PropertyControlResolver(window);
        var value = resolver.Resolve(typeof(FieldElement).GetProperty(nameof(FieldElement.StringValue)), field, false, null, context);
        var syntax = resolver.Resolve(typeof(FieldElement).GetProperty(nameof(FieldElement.SyntaxHighlighting)), field, false, null, context);
        value.AllowHasChangesUpdate = true;
        syntax.AllowHasChangesUpdate = true;
        content.Children.Add(value.Control);
        content.Children.Add(syntax.Control);
        await Task.Delay(100);
        window.UpdateLayout();
        check(!value.HasChanges && !syntax.HasChanges,
            "Displaying an input field and its default syntax does not report unsaved changes");

        content.Children.Remove(value.Control);
        content.Children.Remove(syntax.Control);
        await Task.Delay(50);
        content.Children.Add(value.Control);
        content.Children.Add(syntax.Control);
        await Task.Delay(100);
        window.UpdateLayout();
        check(!value.HasChanges && !syntax.HasChanges,
            "Reloading field editors without editing keeps their change tracking clean");

        check(!MenuVisuals<TextBox>(syntax.Control).Any(),
            "Syntax highlighting offers a single selection control without a free-text editor");

        var selector = MenuVisuals<SelectionDropDownControl>(syntax.Control).Single();
        var choices = SelectionDropDown.CreateMenu(selector, selector.ItemsSource);
        choices.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "JQL"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(50);
        check(field.SyntaxHighlighting == "JQL" && syntax.HasChanges,
            "Choosing a syntax language updates the model and reports an edit");

        syntax.HasChanges = false;
        choices = SelectionDropDown.CreateMenu(selector, selector.ItemsSource);
        choices.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "\u00a0"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(50);
        check(field.SyntaxHighlighting == null && selector.SelectedValue == null && syntax.HasChanges
            && MenuVisuals<ICSharpCode.AvalonEdit.TextEditor>(value.Control).Single().SyntaxHighlighting == null,
            "An empty syntax selection stays null while the value editor renders plain text");

        var suggestions = MenuVisuals<SelectionDropDownControl>(value.Control).Single();
        check(suggestions.IsSuggestion && !suggestions.HasMatchingSuggestion,
            "The optional suggestion selector identifies its purpose before a suggestion is chosen");
        var examples = SelectionDropDown.CreateMenu(suggestions, suggestions.ItemsSource);
        examples.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Example"))
            .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Task.Delay(50);
        check(field.StringValue == "example" && value.HasChanges && suggestions.HasMatchingSuggestion,
            "Choosing a suggestion fills the editor and activates its selected badge");
        window.Width = 700;
        window.Height = 460;
        window.UpdateLayout();
        SaveMenuImage(directory, window, "field-selected-suggestion.png");

        var code = MenuVisuals<ICSharpCode.AvalonEdit.TextEditor>(value.Control).Single();
        code.Text = "edited value";
        check(field.StringValue == "edited value" && value.HasChanges && !suggestions.HasMatchingSuggestion,
            "Editing a custom value clears the suggestion badge state and still reports an edit");
        SaveMenuImage(directory, window, "field-custom-value.png");
    }
}
