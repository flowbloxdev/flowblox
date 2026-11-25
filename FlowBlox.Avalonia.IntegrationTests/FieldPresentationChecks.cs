using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Resolver;
using FlowBlox.UICore.Views;
using ICSharpCode.AvalonEdit;
using MahApps.Metro.IconPacks;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckFieldPresentationAsync(Window window, StackPanel content, Action<bool, string> check)
    {
        var field = new FieldElement(FieldTypes.Text)
        {
            Name = "Presentation",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            StringValue = "preserve"
        };
        var resolver = new PropertyControlResolver(window);
        var model = resolver.Resolve(
            typeof(FieldElement).GetProperty(nameof(FieldElement.StringValue)),
            field, false, null, new PropertyControlBindingContext(field));
        content.Children.Add(model.Control);
        await Task.Delay(50);
        var editor = MenuVisuals<TextEditor>(model.Control).Single();
        var grip = MenuVisuals<ResizeGrip>(model.Control).Single();
        check(editor.Height == 24 && !editor.ShowLineNumbers && grip.Visibility == Visibility.Collapsed,
            "Default field editor has one line and no resize grip");
        var enter = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(editor), 0, Key.Enter)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        editor.RaiseEvent(enter);
        check(enter.Handled, "Single-line field editor prevents entering a new line");

        field.DisplayMode = FieldDisplayMode.List;
        await Task.Delay(50);
        check(double.IsNaN(editor.Height) && editor.ShowLineNumbers && grip.Visibility == Visibility.Visible
            && field.StringValue == "preserve",
            "Switching to list updates the existing editor through binding and preserves its value");
        field.DisplayMode = FieldDisplayMode.Multiline;
        field.StringValue = "first\nsecond";
        FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
        field.DisplayMode = FieldDisplayMode.SingleLine;
        await Task.Delay(50);
        check(editor.Height == 24 && !editor.ShowLineNumbers && editor.Text == "first\nsecond",
            "Switching back to one line preserves existing multiline content without rewriting the model");
        content.Children.Remove(model.Control);

        var project = new FlowBloxProject { ProjectName = "List presentation checks" };
        field.DisplayMode = FieldDisplayMode.List;
        field.ListSeparator = ";";
        field.DefaultValue = "first;second;keep";
        project.UserFields = [field];
        project.FlowBloxRegistry.Register(field);
        var execution = new ProjectExecutionWindow(new ProjectShortcut { Name = project.ProjectName }, project)
        {
            Owner = window
        };
        try
        {
            execution.Show();
            await Task.Delay(100);
            var fields = (StackPanel)execution.FindName("FieldsPanel");
            var rows = MenuVisuals<TextEditor>(fields).ToList();
            check(rows.Count == 3 && rows[0].Text == "first" && rows[2].Text == "keep",
                "Execution window renders configured list defaults as individual field editors");
            rows[0].Text = "pasted;next";
            await Task.Delay(100);
            rows = MenuVisuals<TextEditor>(fields).ToList();
            check(rows.Count == 3 && rows[0].Text == "pasted" && rows[1].Text == "next"
                && rows[2].Text == "keep" && field.StringValue == "pasted;next;keep",
                "Execution list paste overwrites existing rows from the active row and keeps trailing values");
            rows[2].Text = "third;fourth;fifth";
            await Task.Delay(100);
            check(MenuVisuals<TextEditor>(fields).Count() == 5 && field.StringValue == "pasted;next;third;fourth;fifth",
                "Execution list paste creates only the missing rows and publishes the serialized input");
            field.StringValue = "restored;configuration";
            FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
            await Task.Delay(100);
            rows = MenuVisuals<TextEditor>(fields).ToList();
            check(rows.Count == 2 && rows[0].Text == "restored" && rows[1].Text == "configuration",
                "Restoring an execution input configuration rebuilds list rows from its current value");
            foreach (var remove in MenuVisuals<Button>(fields)
                .Where(button => button.Content is PackIconMaterial icon && icon.Kind == PackIconMaterialKind.Minus)
                .ToList())
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Task.Delay(100);
            rows = MenuVisuals<TextEditor>(fields).ToList();
            check(rows.Count == 3 && rows[0].Text == "first" && field.StringValue == field.DefaultValue,
                "Clearing a list restores its configured fallback in both the model and visible rows");
        }
        finally
        {
            execution.Close();
        }
    }
}
