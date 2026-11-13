using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util.FlowBlocks;
using FlowBlox.UICore.Resolver;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckPatternGridDirtyTrackingAsync(Action<bool, string> check)
    {
        using var registryScope = FlowBloxRegistryProvider.BeginScopedRegistry(new FlowBloxProject().FlowBloxRegistry);
        var pattern = new StartEndPattern { StartPattern = "Before" };
        var block = new StartEndPatternSelectorFlowBlock { Name = "Patterns" };
        block.StartEndPatterns.Add(pattern);
        var window = new Window { Width = 1050, Height = 350, ShowInTaskbar = false };
        var patterns = new PropertyControlResolver(window).Resolve(
            block.GetType().GetProperty(nameof(block.StartEndPatterns)),
            block,
            false,
            null,
            new PropertyControlBindingContext(block));
        patterns.AllowHasChangesUpdate = true;
        window.Content = patterns.Control;
        var collectionNotifications = 0;
        block.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(block.StartEndPatterns))
                collectionNotifications++;
        };

        try
        {
            window.Show();
            await Task.Delay(150);
            window.UpdateLayout();
            var grid = FindFieldSelectionVisual<DataGrid>(window);
            check(!patterns.HasChanges && collectionNotifications == 0,
                "Rendering pattern grid editors leaves the FlowBlock property view clean");

            var startColumn = grid.Columns.Single(column => column.SortMemberPath == nameof(pattern.StartPattern));
            var text = FindFieldSelectionVisual<TextBox>(startColumn.GetCellContent(pattern));
            text.Text = "After";
            check(pattern.StartPattern == "After" && patterns.HasChanges && collectionNotifications > 0,
                "Editing an auto-property in display mode marks the owning FlowBlock property view dirty");
            grid.CommitEdit();
            check(patterns.HasChanges, "Committing the grid edit preserves the FlowBlock dirty state");

            patterns.HasChanges = false;
            var indexColumn = grid.Columns.Single(column => column.SortMemberPath == nameof(pattern.Index));
            var indexCell = indexColumn.GetCellContent(pattern);
            var index = FindFieldSelectionVisual<TextBox>(indexCell);
            index.Text = "2";
            check(pattern.Index == 2 && patterns.HasChanges,
                "Editing a nullable scalar auto-property propagates dirty state to the FlowBlock");

            patterns.HasChanges = false;
            pattern.SetPropertySelectedFieldName(nameof(pattern.Index), "$User::Index");
            check(patterns.HasChanges, "Selecting a scalar field marks the owning FlowBlock dirty");

            patterns.HasChanges = false;
            var unlink = FindFieldSelectionVisual<System.Windows.Controls.Grid>(indexCell)
                .Children.OfType<Button>().Last();
            unlink.Command.Execute(null);
            check(pattern.GetPropertySelectedFieldName(nameof(pattern.Index)) == null && patterns.HasChanges,
                "Unlinking a scalar field marks the owning FlowBlock dirty");
        }
        finally
        {
            window.Close();
        }

        await Task.Delay(150);
        var notificationsAfterClose = collectionNotifications;
        FlowBloxComponentHelper.RaisePropertyChanged(pattern, nameof(pattern.StartPattern));
        check(collectionNotifications == notificationsAfterClose,
            "Unloaded pattern editors stop forwarding row changes to the FlowBlock");
    }
}
