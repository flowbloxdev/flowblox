using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.Core.Models.FlowBlocks.Base;
using FlowBlox.Core.Models.FlowBlocks.IO;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.UICore.Factory.PropertyView;
using FlowBlox.UICore.Utilities;
using MahApps.Metro.Controls;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckLiteralGridRenderingAsync(Action<bool, string> check)
    {
        await CheckPatternGridDirtyTrackingAsync(check);

        using var registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        // WPF Loaded callbacks do not inherit the AsyncLocal registry scope.
        var transaction = FlowBloxRegistryProvider.OpenTransaction(detached: true);
        try
        {
            await CheckLiteralGridRenderingInTransactionAsync(check);
        }
        finally
        {
            FlowBloxRegistryProvider.CancelTransaction(transaction);
        }
    }

    private async Task CheckLiteralGridRenderingInTransactionAsync(Action<bool, string> check)
    {
        var writer = new TableWriterFlowBlock { Name = "Writer" };
        var schemaProperty = writer.GetType().GetProperty(nameof(writer.TableColumnDefinitions));
        var schemaControl = new DataGridFactory(null, schemaProperty, writer, false).Create();
        var schemaWindow = new Window { Content = schemaControl, Width = 1050, Height = 350, ShowInTaskbar = false };
        try
        {
            schemaWindow.Show();
            await Task.Delay(150);
            schemaWindow.UpdateLayout();
            var grid = FindFieldSelectionVisual<DataGrid>(schemaWindow);
            check(grid.Columns.Select(column => column.SortMemberPath).SequenceEqual(new[]
            {
                nameof(TableColumnDefinition.Field),
                nameof(TableColumnDefinition.IsRequired),
                nameof(TableColumnDefinition.IsKeyColumn),
                nameof(TableColumnDefinition.ColumnName)
            }), "Empty table schema retains all four columns without enabling field references");

            var row = new TableColumnDefinition { ColumnName = "Ticket" };
            writer.TableColumnDefinitions.Add(row);
            await Task.Delay(150);
            grid.UpdateLayout();
            var text = FindFieldSelectionVisual<TextBox>(grid.Columns[3].GetCellContent(row));
            check(text.Text == "Ticket", "Literal schema column displays its configured name");
            text.Text = "Issue";
            var toggle = FindFieldSelectionVisual<ToggleSwitch>(grid.Columns[1].GetCellContent(row));
            toggle.IsOn = true;
            check(row.ColumnName == "Issue" && row.IsRequired,
                "Literal string and boolean grid editors update the schema model");
        }
        finally
        {
            schemaWindow.Close();
        }

        var assignment = new InputBehaviorAssignment { FlowBlock = writer };
        var emptyAssignment = new InputBehaviorAssignment();
        writer.InputBehaviorAssignments.Add(assignment);
        writer.InputBehaviorAssignments.Add(emptyAssignment);
        var behaviorProperty = writer.GetType().GetProperty(nameof(writer.InputBehaviorAssignments));
        var behaviorControl = new DataGridFactory(null, behaviorProperty, writer, false).Create();
        var behaviorWindow = new Window { Content = behaviorControl, Width = 800, Height = 350, ShowInTaskbar = false };
        try
        {
            behaviorWindow.Show();
            await Task.Delay(150);
            behaviorWindow.UpdateLayout();
            var grid = FindFieldSelectionVisual<DataGrid>(behaviorWindow);
            check(grid.Columns.Count == 2, "Input behavior retains both the reference and literal enum columns");
            var referenceCell = grid.Columns[0].GetCellContent(assignment);
            check(grid.Columns[0].IsReadOnly
                && FindFieldSelectionVisual<TextBlock>(referenceCell)?.Text == writer.Name
                && FindFieldSelectionVisual<SelectionDropDownControl>(referenceCell) == null,
                "Read-only FlowBlock reference displays its selected name without a dropdown");
            var emptyCell = grid.Columns[0].GetCellContent(emptyAssignment);
            check(FindFieldSelectionVisual<TextBlock>(emptyCell)?.Text == string.Empty,
                "Empty read-only reference remains blank");
        }
        finally
        {
            behaviorWindow.Close();
        }
    }
}
