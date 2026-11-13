using FlowBlox.Core.Extensions;
using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FlowBlox.Core.Attributes;
using FlowBlox.UICore.Resolver;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckSimplePropertyFieldSelection(Action<bool, string> check)
    {
        var settings = new SimplePropertyFieldSelectionSettings();
        var property = typeof(SimplePropertyFieldSelectionSettings).GetProperty(nameof(settings.Enabled));
        var editor = new SimplePropertyInputControlWithOptionalButtonsCreator(null);
        var grid = (System.Windows.Controls.Grid)editor.CreateSimplePropertyInputControlWithOptionalButtons(
            property,
            settings,
            property.GetFlowBloxUIAttribute(),
            new Binding(property.Name) { Source = settings },
            false);
        var host = (System.Windows.Controls.Grid)grid.Children[0];
        var literal = (FrameworkElement)host.Children[0];
        var selection = (StackPanel)host.Children[1];
        var unlink = grid.Children.OfType<Button>().Last();
        check(literal.Visibility == Visibility.Visible && selection.Visibility == Visibility.Collapsed && !unlink.IsEnabled,
            "Simple property without a companion property starts with its literal editor");

        settings.SetPropertySelectedFieldName(property.Name, "$USER::Enabled");
        check(literal.Visibility == Visibility.Collapsed && selection.Visibility == Visibility.Visible && unlink.IsEnabled,
            "Map selection replaces the literal editor and enables unlink immediately");
        check(selection.Children.OfType<TextBlock>().Single().Text == "$USER::Enabled",
            "Field editor displays the saved field name even when unresolved");

        unlink.Command.Execute(null);
        check(settings.GetPropertySelectedFieldName(property.Name) == null && literal.Visibility == Visibility.Visible && !unlink.IsEnabled,
            "Unlink removes the map selection and restores the literal editor");
    }
}
