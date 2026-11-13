using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using FlowBlox.Core.Models.Project;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckManageInputFilesRemovalAsync(Action<bool, string> check)
    {
        var first = new FlowBloxInputFile { RelativePath = "first.txt" };
        var retained = new FlowBloxInputFile { RelativePath = "retained.txt" };
        var last = new FlowBloxInputFile { RelativePath = "last.txt" };
        var project = new FlowBloxProject
        {
            InputFiles = [first, retained, last]
        };
        var dialog = new ManageInputFilesWindow(project)
        {
            ShowInTaskbar = false
        };

        try
        {
            dialog.Show();
            await Task.Delay(150);
            dialog.UpdateLayout();

            var viewModel = (ManageInputFilesViewModel)dialog.DataContext;
            var grid = (DataGrid)dialog.FindName("InputFilesGrid");
            grid.SelectedItems.Add(first);
            grid.SelectedItems.Add(last);
            var removeButton = MenuVisuals<Button>(dialog)
                .Single(button => ReferenceEquals(button.Command, viewModel.RemoveInputFileCommand));

            check(removeButton.Command.CanExecute(removeButton.CommandParameter),
                "Input file removal is enabled for a multiple selection");
            removeButton.Command.Execute(removeButton.CommandParameter);

            check(viewModel.InputFiles.SequenceEqual(new[] { retained })
                && project.InputFiles.SequenceEqual(new[] { retained })
                && grid.SelectedItems.Count == 0,
                "Input file removal deletes every selected entry from the grid and project while retaining unselected files");

            var readOnlyViewModel = new ManageInputFilesViewModel(null, project, isReadOnly: true)
            {
                SelectedInputFile = retained
            };
            readOnlyViewModel.RemoveInputFileCommand.Execute(new[] { retained });
            check(readOnlyViewModel.InputFiles.SequenceEqual(new[] { retained })
                && project.InputFiles.SequenceEqual(new[] { retained }),
                "Read-only input file management rejects removal even when the command is invoked directly");
        }
        finally
        {
            dialog.Close();
        }
    }
}
