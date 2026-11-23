using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.Utilities;
using FlowBlox.UICore.ViewModels.PropertyView;
using FlowBlox.UICore.Views;
using MahApps.Metro.IconPacks;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private static async Task CheckListViewEditRefreshAsync(Window owner, string directory, Action<bool, string> check)
    {
        var previousProject = FlowBloxProjectManager.Instance.ActiveProject;
        var project = new FlowBloxProject();
        FlowBloxProjectManager.Instance.ActiveProject = project;
        var block = new SplitFlowBlock { Name = "SyntheticSplit" };
        block.Separators.Add(new SplitSeparatorDefinition { Separator = "," });
        block.Separators.Add(new SplitSeparatorDefinition { Separator = "|" });
        project.FlowBloxRegistry.RegisterFlowBlock(block);
        var editor = new PropertyWindow(new PropertyWindowArgs(block)) { Owner = owner };
        var checks = ExerciseSeparatorListAsync(editor, block, directory, check);
        try
        {
            editor.ShowDialog();
            await checks;
        }
        finally
        {
            editor.Close();
            FlowBloxProjectManager.Instance.ActiveProject = previousProject;
        }
    }

    private static async Task ExerciseSeparatorListAsync(PropertyWindow editor, SplitFlowBlock original, string directory, Action<bool, string> check)
    {
        try
        {
            await Task.Delay(250);
            var model = (PropertyWindowViewModel)editor.DataContext;
            var working = (SplitFlowBlock)model.PropertyViewModel.WorkingCopy;
            var list = MenuVisuals<ListView>(editor).Single(view => ReferenceEquals(view.ItemsSource, working.Separators));
            var row = working.Separators[0];
            list.SelectedItem = row;
            var control = model.PropertyViewModel.Tabs.SelectMany(tab => tab.Controls)
                .Single(item => item.PropertyName == nameof(SplitFlowBlock.Separators)).Control;
            var edit = MenuVisuals<Button>(control).Single(button => button.Content is PackIconMaterial icon && icon.Kind == PackIconMaterialKind.Pencil);

            var child = EditSeparatorDialogAsync(editor, string.Empty, SpecialSeparator.Tab, true, check);
            edit.Command.Execute(null);
            await child;
            editor.UpdateLayout();
            check(row.Separator == string.Empty && row.SpecialSeparator == SpecialSeparator.Tab,
                "Editing a Split separator saves its reactive-only values in place");
            var container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
            check(MenuVisuals<TextBlock>(container).Any(text => text.Text == nameof(SpecialSeparator.Tab)),
                "Saving a Split separator refreshes its visible Special column without item notifications");
            check(ReferenceEquals(list.SelectedItem, row) && working.Separators.Count == 2 && working.Separators[1].Separator == "|",
                "Refreshing an edited separator preserves selection, collection order and other rows");

            child = EditSeparatorDialogAsync(editor, string.Empty, SpecialSeparator.NewLine, false, check);
            edit.Command.Execute(null);
            await child;
            editor.UpdateLayout();
            container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
            check(row.SpecialSeparator == SpecialSeparator.Tab && MenuVisuals<TextBlock>(container).Any(text => text.Text == nameof(SpecialSeparator.Tab)),
                "Cancelling a separator edit preserves its value and displayed row");

            child = EditSeparatorDialogAsync(editor, ";", null, true, check);
            edit.Command.Execute(null);
            await child;
            editor.UpdateLayout();
            container = (ListViewItem)list.ItemContainerGenerator.ContainerFromItem(row);
            var texts = MenuVisuals<TextBlock>(container).Select(text => text.Text).ToList();
            check(row.SpecialSeparator == null && texts.Contains(";") && !texts.Contains(nameof(SpecialSeparator.Tab)),
                "Saving a regular separator refreshes its text and clears the previous Special cell");
            check(original.Separators[0].Separator == "," && original.Separators[0].SpecialSeparator == null,
                "List-row edits remain inside the parent Split property transaction");
            SaveMenuImage(directory, editor, "split-separator-list-after-edit.png");
        }
        finally
        {
            editor.Close();
        }
    }

    private static async Task EditSeparatorDialogAsync(PropertyWindow owner, string separator, SpecialSeparator? special, bool save, Action<bool, string> check)
    {
        await Task.Delay(150);
        var dialog = Application.Current.Windows.OfType<PropertyWindow>().Single(window => window.Owner == owner);
        try
        {
            var model = (PropertyWindowViewModel)dialog.DataContext;
            var controls = model.PropertyViewModel.Tabs.SelectMany(tab => tab.Controls).ToList();
            var text = MenuVisuals<TextBox>(controls.Single(item => item.PropertyName == nameof(SplitSeparatorDefinition.Separator)).Control).Single();
            var selection = MenuVisuals<EnumerationDropDownControl>(controls.Single(item => item.PropertyName == nameof(SplitSeparatorDefinition.SpecialSeparator)).Control).Single();
            text.Text = separator;
            selection.SelectedValue = special;
            check(model.SaveCommand.CanExecute(null), "Editing separator controls enables the nested property editor Save action");
            if (save)
                model.SaveCommand.Execute(null);
            else
                model.CancelCommand.Execute(null);
            for (var attempt = 0; attempt < 30 && dialog.IsVisible; attempt++)
                await Task.Delay(20);
            check(!dialog.IsVisible, "The separator edit dialog completes its save/cancel lifecycle");
        }
        finally
        {
            dialog.Close();
        }
    }
}
