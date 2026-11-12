using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using global::Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using WpfPanel = System.Windows.Controls.StackPanel;
using FlowBlox.UICore.Utilities;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckProjectLauncherAsync(Action<bool, string> check, string directory, string sourceProjectPath)
    {
        // Let activation refresh finish before inserting the isolated test sections.
        _window.Topmost = true;
        _window.Activate();
        await Task.Delay(500);
        var host = _window.FindControl<ScrollViewer>("Shortcuts");
        check(host != null && host.IsVisible && !Workspace.IsVisible, "Launcher is visible only without an active project");
        var rows = Enumerable.Range(1, 22).Select(index => new ProjectShortcutItem
        {
            Project = new ProjectShortcut
            {
                Name = index == 22 ? "A much longer project name at the bottom of the list" : $"Local example {index}",
                ProjectFile = "test.fbprj",
                ProjectSpaceGuid = index == 1 ? Guid.NewGuid().ToString() : string.Empty
            },
            IconWidth = 36,
            Execution = index == 1 ? new ProjectExecutionEntry() : null
        }).ToList();
        _window.ShortcutSections.Clear();
        foreach (var title in new[] { "Local projects", "Recently opened", "Example flows", "Project Space favorites", "Executions" })
            _window.ShortcutSections.Add(new ProjectShortcutSection { Title = title, Items = rows });
        _window.Width = 800;
        _window.Height = 600;
        await Task.Delay(250);
        var boxes = _window.FindControl<ItemsControl>("ShortcutBoxes");
        check(host.Extent.Width <= host.Viewport.Width + 1, "Minimum-size launcher has no horizontal scrolling");
        var lists = boxes.GetVisualDescendants().OfType<ListBox>().ToList();
        check(lists.Count == 5, "All launcher categories share the same box template");
        check(lists.All(x => x.Bounds.Height < 240), "Project lists stay bounded inside equal-height boxes");
        var initialWidth = lists[0].Bounds.Width;
        lists[0].ScrollIntoView(rows.Last());
        await Task.Delay(150);
        check(Math.Abs(lists[0].Bounds.Width - initialWidth) < 1,
            "Scrolling to the longest project does not resize the shortcut box");
        lists[0].ScrollIntoView(rows.First());
        await Task.Delay(150);
        var firstRow = lists[0].GetVisualDescendants().OfType<ListBoxItem>().First();
        var runningName = firstRow.GetVisualDescendants().OfType<TextBlock>()
            .Single(label => label.Classes.Contains("shortcutName"));
        var runningLoader = firstRow.GetVisualDescendants().OfType<ProgressBar>().Single();
        check(runningName.Bounds.Width >= 80 && runningLoader.Bounds.Width <= 16
            && runningLoader.TranslatePoint(default, firstRow).Value.X
                >= runningName.TranslatePoint(default, firstRow).Value.X + runningName.Bounds.Width,
            "Running execution names remain readable with a bounded loader to their right");
        var actions = firstRow.GetVisualDescendants().OfType<StackPanel>()
            .Single(panel => panel.Classes.Contains("shortcutActions"));
        var previousCursorPosition = System.Windows.Forms.Cursor.Position;
        try
        {
            var hoverPoint = firstRow.PointToScreen(new global::Avalonia.Point(50, 10));
            System.Windows.Forms.Cursor.Position = new System.Drawing.Point(hoverPoint.X, hoverPoint.Y);
            await Task.Delay(300);
            using (var screenshot = CaptureDesktopWindow())
                screenshot.Save(Path.Combine(directory, "launcher-hover.png"));
            check(!firstRow.IsSelected && actions.Opacity == 1 && actions.IsHitTestVisible,
                $"Hover reveals shortcut actions without selecting the project (over={firstRow.IsPointerOver}, selected={firstRow.IsSelected}, opacity={actions.Opacity}, hit={actions.IsHitTestVisible}, point={hoverPoint})");
        }
        finally
        {
            System.Windows.Forms.Cursor.Position = previousCursorPosition;
        }
        lists[0].SelectedIndex = 0;
        await Task.Delay(100);
        check(Math.Abs(lists[0].Bounds.Width - initialWidth) < 1 && actions.Opacity == 1,
            "Selecting a project keeps box width stable and reveals its actions");
        var scrollbar = lists[0].GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.ScrollBar>()
            .First(bar => bar.Orientation == global::Avalonia.Layout.Orientation.Vertical && bar.IsVisible);
        var lastButton = actions.Children.OfType<Button>().Last(button => button.IsVisible);
        var buttonRight = lastButton.TranslatePoint(default, lists[0]).Value.X + lastButton.Bounds.Width;
        var scrollbarLeft = scrollbar.TranslatePoint(default, lists[0]).Value.X;
        check(buttonRight + 4 <= scrollbarLeft, "Shortcut actions leave a gap before the vertical scrollbar");
        using (var screenshot = CaptureDesktopWindow())
            screenshot.Save(Path.Combine(directory, "launcher-minimum-size.png"));
        _window.Width = 1420;
        _window.Height = 1000;
        await Task.Delay(250);
        using (var screenshot = CaptureDesktopWindow())
            screenshot.Save(Path.Combine(directory, "launcher-desktop.png"));
        _window.Topmost = false;

        CheckFieldSelectionWithoutRegistry(check);
        CheckFieldSelectionDialogScrolling(check, directory);
        await CheckLoadedProjectExecutionInputsAsync(check, directory, sourceProjectPath);

        var project = new FlowBloxProject { ProjectName = "Launcher input preview", ProjectDescription = "Isolated execution configuration" };
        var field = new FieldElement(FieldTypes.Text)
        {
            Name = "Choice",
            UserField = true,
            UserFieldType = UserFieldTypes.Input,
            IsRequiredAtRuntimeStartup = true,
            LoVType = LoVType.PossibleValues,
            ListOfValues = [new ValueItem { DisplayName = "Example choice", Value = "example" }]
        };
        project.FlowBloxRegistry.Register(field);
        var storageDirectory = Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(storageDirectory);
        var projectFile = Path.Combine(storageDirectory, "inputs.fbprj");
        project.Save(projectFile);
        var shortcut = ProjectShortcut.FromFile(projectFile);
        project = await shortcut.LoadAsync();
        field = project.UserFields.Single();
        var store = new ProjectExecutions(Path.Combine(storageDirectory, "fbexecutions.json"));
        var dialog = new ProjectExecutionWindow(shortcut, project, store);
        var closedAfterStart = false;
        dialog.Closed += (_, _) => closedAfterStart = true;
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var fields = (WpfPanel)dialog.FindName("FieldsPanel");
                check(FindLauncherVisuals<WpfTextBlock>(fields).Any(x => x.Text == "Choice *"),
                    "Execution inputs show required markers next to the field name");
                check(FindLauncherVisuals<SelectionDropDownControl>(fields).Any(),
                    "Execution inputs reuse the possible-values combo-box editor");
                check(FlowBloxProjectManager.Instance.ActiveProject == null,
                    "Previewing execution parameters does not load a designer project");
                var execute = (WpfButton)dialog.FindName("ExecuteButton");
                check(!execute.IsEnabled, "Execution stays disabled while a required input is empty");
                var remove = (WpfButton)dialog.FindName("RemoveOptionButton");
                var options = (System.Windows.Controls.DataGrid)dialog.FindName("OptionsGrid");
                check(!remove.IsEnabled, "Removing option overrides is disabled without a selection");
                ((WpfButton)dialog.FindName("AddOptionButton")).RaiseEvent(new System.Windows.RoutedEventArgs(WpfButton.ClickEvent));
                check(remove.IsEnabled, "Selecting an option override enables its removal");
                remove.RaiseEvent(new System.Windows.RoutedEventArgs(WpfButton.ClickEvent));
                check(!remove.IsEnabled && options.Items.Count == 0,
                    "Removing the selected override disables deletion again");
                field.StringValue = "example";
                FlowBlox.Core.Util.FlowBlocks.FlowBloxComponentHelper.RaisePropertyChanged(field, nameof(FieldElement.StringValue));
                check(execute.IsEnabled, "Filling a required input immediately enables execution");
                CaptureProjectCards(dialog, Path.Combine(directory, "project-execution-inputs.png"));
                ((WpfButton)dialog.FindName("ExecuteButton")).RaiseEvent(new System.Windows.RoutedEventArgs(WpfButton.ClickEvent));
                check(store.GetAll().Count == 1, "Execution is persisted before the runner is monitored");
                check(closedAfterStart, "A successful runner launch closes the configuration dialog");
                check(store.GetAll().Single().Request.UserFields["Choice"] == "example",
                    "Input values are passed to the runner request");
            }
            catch (Exception exception) { failure = exception; }
            finally { dialog.Close(); }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;

        for (var attempt = 0; attempt < 40 && store.GetAll().Single().IsRunning; attempt++)
        {
            await Task.Delay(250);
            store.Refresh();
        }
        check(!store.GetAll().Single().IsRunning, "External runner completion is discovered from the response file");
        check(File.Exists(store.GetAll().Single().RequestFile), "Runner request survives closing the execution dialog");
        check(File.Exists(store.GetAll().Single().ResponseFile), "Runner response survives closing the execution dialog");
        CheckExecutionCompletionViews(check, directory);

        var favorites = new ProjectSpaceFavorites(Path.Combine(storageDirectory, "favorites.json"));
        var favoriteDialog = new PSProjectsWindow(loadRemoteData: false, favorites: favorites);
        var model = (PSProjectsViewModel)favoriteDialog.DataContext;
        model.SearchResults = [new FbProjectResult { Guid = Guid.NewGuid().ToString(), Name = "Favorite test" }];
        favoriteDialog.ContentRendered += (_, _) =>
        {
            try
            {
                var star = FindLauncherVisuals<System.Windows.Controls.Primitives.ToggleButton>(favoriteDialog)
                    .First(x => x.DataContext is FbProjectResult);
                star.IsChecked = true;
                star.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                check(favorites.GetAll().Count == 1 && !favoriteDialog.DialogResult.HasValue,
                    "Project Space star persists a favorite without opening the project card");
            }
            catch (Exception exception) { failure = exception; }
            finally { favoriteDialog.Close(); }
        };
        WpfDialogHost.ShowDialog(favoriteDialog, _window);
        if (failure != null)
            throw failure;
        await Task.Delay(300);
        await CheckOwnProjectSpaceShortcutsAsync(check, directory);
    }

    private async Task CheckLoadedProjectExecutionInputsAsync(Action<bool, string> check, string directory, string projectFile)
    {
        var sourceHash = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(projectFile));
        var shortcut = ProjectShortcut.FromFile(projectFile);
        var project = await shortcut.LoadAsync();
        var inputs = project.UserFields.Where(field => field.IsUserInputField()).ToList();
        var dialog = new ProjectExecutionWindow(shortcut, project);
        Exception failure = null;
        dialog.ContentRendered += (_, _) =>
        {
            try
            {
                var fields = (WpfPanel)dialog.FindName("FieldsPanel");
                var labels = FindLauncherVisuals<WpfTextBlock>(fields).Select(label => label.Text).ToList();
                check(inputs.All(field => labels.Contains(field.Name + (field.IsRequiredAtRuntimeStartup ? " *" : string.Empty))),
                    "Every deserialized input is displayed with its field name and required marker");
                check(((WpfTextBlock)dialog.FindName("ProjectDescription")).Text == project.ProjectDescription,
                    "Execution preview displays the description from the loaded project file");
                check(((System.Windows.Controls.TextBox)dialog.FindName("InputPath")).Text == project.ProjectInputDirectory
                    && ((System.Windows.Controls.TextBox)dialog.FindName("OutputPath")).Text == project.ProjectOutputDirectory,
                    "Execution preview displays both project directories next to their open commands");
                CaptureProjectCards(dialog, Path.Combine(directory, "project-execution-loaded-project.png"));
            }
            catch (Exception exception) { failure = exception; }
            finally { dialog.Close(); }
        };
        WpfDialogHost.ShowDialog(dialog, _window);
        if (failure != null)
            throw failure;
        check(sourceHash.SequenceEqual(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(projectFile))),
            "Previewing execution inputs preserves the source project file");
    }

    private static IEnumerable<T> FindLauncherVisuals<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T value)
            yield return value;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (var child in FindLauncherVisuals<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, index)))
                yield return child;
        }
    }
}
