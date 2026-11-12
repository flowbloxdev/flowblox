using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Util.Fields;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Factory.Adapter;
using FlowBlox.UICore.Resolver;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckEnabledFieldSelectionEditorsAsync(Action<bool, string> check)
    {
        var settings = new SimplePropertyFieldSelectionSettings();
        var resolver = new PropertyControlResolver(null);
        var context = new PropertyControlBindingContext(settings);
        foreach (var property in settings.GetType().GetProperties().Where(FlowBloxFieldSelection.Supports))
        {
            if (property.GetCustomAttribute<System.ComponentModel.DataAnnotations.DisplayAttribute>() == null)
                continue;

            var viewModel = resolver.Resolve(property, settings, false, null, context);
            var grid = viewModel.Control as System.Windows.Controls.Grid;
            if (property.Name == nameof(settings.Choice))
            {
                var host = (ContentControl)viewModel.Control;
                grid = host.Content as System.Windows.Controls.Grid;
            }

            check(grid != null && grid.Children.OfType<Button>().Any(),
                $"Field button is available for explicitly enabled property {property.Name}");

            if (FlowBloxFieldSelection.SupportsScalar(property.PropertyType))
            {
                settings.SetPropertySelectedFieldName(property.Name, "$User::Value");
                var selectionHost = (System.Windows.Controls.Grid)grid.Children[0];
                check(selectionHost.Children[0].Visibility == Visibility.Collapsed
                    && selectionHost.Children[1].Visibility == Visibility.Visible,
                    $"{property.Name} replaces its literal editor with the selected field");
                var unlink = grid.Children.OfType<Button>().Last();
                unlink.Command.Execute(null);
                check(settings.GetPropertySelectedFieldName(property.Name) == null,
                    $"{property.Name} unlinks through the shared map");
            }
        }

        foreach (var component in new FlowBlox.Core.Models.Base.FlowBloxComponent[]
        {
            new FlowBlox.Core.Models.FlowBlocks.SequenceFlow.StartFlowBlock(),
            new FlowBlox.Core.Models.FlowBlocks.TextOperations.FormatFlowBlock(),
            new FlowBlox.Core.Models.Components.FieldElement()
        })
        {
            var nameProperty = component.GetType().GetProperty(nameof(component.Name));
            var nameEditor = resolver.Resolve(nameProperty, component, false, null, new PropertyControlBindingContext(component));
            check(FindFieldSelectionVisual<Button>(nameEditor.Control) == null,
                $"{component.GetType().Name} keeps its unenabled Name property literal-only");
        }

        var literalOnlyProperty = settings.GetType().GetProperty(nameof(settings.LiteralOnly));
        var literalOnlyEditor = resolver.Resolve(literalOnlyProperty, settings, false, null, context);
        check(FindFieldSelectionVisual<Button>(literalOnlyEditor.Control) == null,
            "Scalar property without EnableFieldSelection retains only its literal editor");

        var passwordProperty = settings.GetType().GetProperty(nameof(settings.Password));
        var passwordEditor = new PasswordBox();
        var passwordAdapter = new PropertyTextBoxAdapter(settings, passwordProperty, passwordEditor);
        passwordAdapter.Text = "$User::Secret";
        check(settings.Password == "$User::Secret" && passwordEditor.Password == settings.Password,
            "Password field insertion updates the masked editor and the model");

        settings.Rows.Add(new SimplePropertyFieldSelectionSettings());
        var rowsProperty = settings.GetType().GetProperty(nameof(settings.Rows));
        var rowsViewModel = resolver.Resolve(rowsProperty, settings, false, null, context);
        var window = new Window { Content = rowsViewModel.Control, Width = 1050, Height = 350, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Task.Delay(150);
            window.UpdateLayout();
            var dataGrid = FindFieldSelectionVisual<DataGrid>(window);
            check(dataGrid != null && dataGrid.Columns.Count == 5
                && dataGrid.Columns.All(column => column is DataGridTemplateColumn),
                "Grid scalar columns reuse the property editor stack, including boolean, enum and date");
            check(FindFieldSelectionVisual<Button>(dataGrid) != null,
                "Enabled scalar grid editors render inside the real desktop lifecycle");

            var dateCell = dataGrid.Columns[2].GetCellContent(settings.Rows[0]);
            var dateEditor = FindFieldSelectionVisual<ContentControl>(dateCell);
            check(dateEditor.Visibility == Visibility.Collapsed,
                "Inactive scalar grid properties preserve their activation behavior");
            settings.Rows[0].Enabled = true;
            FlowBloxComponentHelper.RaisePropertyChanged(settings.Rows[0], nameof(settings.Enabled));
            check(dateEditor.Visibility == Visibility.Visible,
                "Scalar grid editors react to activation dependency changes");
        }
        finally
        {
            window.Close();
        }
    }

    private static T FindFieldSelectionVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            return match;

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = FindFieldSelectionVisual<T>(VisualTreeHelper.GetChild(root, index));
            if (child != null)
                return child;
        }

        return null;
    }
}
