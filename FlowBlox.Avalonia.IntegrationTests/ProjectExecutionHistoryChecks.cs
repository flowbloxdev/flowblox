using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckProjectExecutionHistory(Action<bool, string> check, string directory)
    {
        CheckProjectNoticeMarkdown(check, directory);
        var location = Path.Combine(directory, "execution-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(location);
        var shortcut = new ProjectShortcut { Name = "History preview", ProjectFile = Path.Combine(location, "project.fbprj") };
        var entry = new ProjectExecutionEntry
        {
            Id = Guid.NewGuid(),
            Project = shortcut,
            StartedUtc = DateTime.UtcNow,
            Response = new RunnerResponse { Success = true, FinishedUtc = DateTime.UtcNow },
            Request = new RunnerRequest
            {
                PasswordFieldNames = ["Secret"],
                UserFields = new() { ["Count"] = "last input", ["Secret"] = "private-history-test", ["Removed"] = "ignore removed field" },
                OptionOverrides = new() { [OptionsConstants.PathsOutputDir] = location }
            }
        };
        var older = new ProjectExecutionEntry
        {
            Id = Guid.NewGuid(),
            Project = shortcut,
            StartedUtc = entry.StartedUtc.AddHours(-1),
            Request = new RunnerRequest { UserFields = new() { ["Count"] = "older input" } }
        };
        var unrelated = new ProjectExecutionEntry
        {
            Id = Guid.NewGuid(),
            Project = new ProjectShortcut { Name = shortcut.Name, ProjectFile = Path.Combine(location, "other.fbprj") },
            StartedUtc = entry.StartedUtc.AddHours(1)
        };
        var path = Path.Combine(location, "history.json");
        new LauncherJsonStore<ProjectExecutionEntry>(path).Update(entries =>
        {
            entries.AddRange([older, unrelated, entry]);
        });
        var store = new ProjectExecutions(path);
        var configurations = new ProjectInputConfigurations(Path.Combine(location, "inputs.json"));
        configurations.Save(new ProjectInputConfiguration { Name = "A Work inputs", Project = shortcut, Request = entry.Request });
        configurations.Save(new ProjectInputConfiguration { Name = "B Other inputs", Project = shortcut, Request = older.Request });
        configurations.Save(new ProjectInputConfiguration { Name = "Unrelated", Project = unrelated.Project, Request = unrelated.Request });
        {
                var project = new FlowBloxProject { ProjectName = shortcut.Name, UserFields = new List<FieldElement>() };
                foreach (var name in new[] { "Count", "Secret", "Added" })
                {
                    var field = new FieldElement(FieldTypes.Text)
                    {
                        Name = name,
                        UserField = true,
                        UserFieldType = UserFieldTypes.Input,
                        IsPassword = name == "Secret",
                        IsRequiredAtRuntimeStartup = name == "Added",
                        DefaultValue = name == "Count" ? "project default" : null
                    };
                    project.FlowBloxRegistry.Register(field);
                    project.UserFields.Add(field);
                }
                var dialog = new ProjectExecutionWindow(shortcut, project, store, inputConfigurations: configurations);
                Exception failure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var history = (ListBox)dialog.FindName("ConfigurationList");
                        var fields = (StackPanel)dialog.FindName("FieldsPanel");
                        check(history.Items.Count == 2 && history.SelectedIndex == -1,
                            "Saved input configurations are scoped to the project and do not silently replace defaults");
                        history.SelectedIndex = 0;
                        var card = (ProjectInputConfigurationItem)history.SelectedItem;
                        check(card.Status.FilledCount == 2 && card.Status.TotalCount == 3 && card.HasMissingRequiredFields,
                            "Configuration cards show filled fields and missing required inputs");
                        check(((Button)dialog.FindName("SaveConfigurationButton")).IsEnabled && !((Button)dialog.FindName("ExecuteButton")).IsEnabled,
                            "Incomplete configurations can be saved while execution remains unavailable");
                        check(project.UserFields.Single(field => field.Name == "Count").StringValue == "last input"
                            && string.IsNullOrEmpty(project.UserFields.Single(field => field.Name == "Added").StringValue),
                            "Saved configuration restores matching inputs while new inputs retain current defaults");
                        check(FindLauncherVisuals<PasswordBox>(fields).Any(),
                            $"Execution inputs always use password masking");
                        var passwordEditor = FindLauncherVisuals<PasswordValueEditor>(fields).Single();
                        var maskButton = (System.Windows.Controls.Primitives.ToggleButton)passwordEditor.FindName("MaskButton");
                        check(maskButton.IsChecked == true && passwordEditor.Text == "private-history-test",
                            "Password inputs start masked and restore the actual history value");
                        maskButton.IsChecked = false;
                        check(((TextBox)passwordEditor.FindName("RevealedEditor")).Visibility == Visibility.Visible
                            && project.UserFields.Single(field => field.Name == "Secret").IsPassword,
                            "The eye reveals the value without changing the field's password flag");
                        check(((DataGrid)dialog.FindName("OptionsGrid")).Items.Count == 1,
                            "Saved configuration restores its option overrides");
                        history.SelectedIndex = 1;
                        check(project.UserFields.Single(field => field.Name == "Count").StringValue == "older input",
                            "Selecting another configuration replaces the current parameter values");
                        ((Button)dialog.FindName("ClearInputsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        check(passwordEditor.Text == string.Empty,
                            "Clearing input history also clears the revealed password editor");
                        check(history.SelectedIndex == -1
                            && project.UserFields.Single(field => field.Name == "Count").StringValue == "project default"
                            && string.IsNullOrEmpty(project.UserFields.Single(field => field.Name == "Secret").StringValue)
                            && ((DataGrid)dialog.FindName("OptionsGrid")).Items.Count == 0,
                            "Clear restores original inputs, clears snapshot selection and removes overrides");
                        QueueConfigurationName(dialog, "Research inputs");
                        ((Button)dialog.FindName("SaveConfigurationButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        check(configurations.GetForProject(shortcut).Count == 3,
                            "Save creates a named input configuration without requiring a project execution");
                        var selected = (ProjectInputConfigurationItem)history.SelectedItem;
                        check(selected.Name == "Research inputs" && selected.HasMissingRequiredFields,
                            "The saved partial configuration is selected and remains visibly incomplete");
                        dialog.UpdateLayout();
                        var container = (ListBoxItem)history.ItemContainerGenerator.ContainerFromItem(selected);
                        var grid = FindLauncherVisuals<System.Windows.Controls.Grid>(container).Single(item => item.ContextMenu != null);
                        var menu = grid.ContextMenu;
                        menu.PlacementTarget = grid;
                        menu.IsOpen = true;
                        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
                        var edit = menu.Items.OfType<MenuItem>().Single();
                        check(edit.Tag is ProjectInputConfigurationItem, "The card context menu addresses the clicked configuration");
                        menu.IsOpen = false;
                        project.UserFields.Single(field => field.Name == "Count").StringValue = "Unsaved edit";
                        QueueConfigurationName(dialog, "Renamed inputs");
                        edit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                        check(configurations.GetForProject(shortcut).Any(item => item.Name == "Renamed inputs")
                            && project.UserFields.Single(field => field.Name == "Count").StringValue == "Unsaved edit",
                            "Renaming through EditValue preserves unsaved input changes");
                        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        dialog.UpdateLayout();
                        CaptureProjectCards(dialog, Path.Combine(directory, "project-input-configurations.png"));
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { dialog.Close(); }
                };
                WpfDialogHost.ShowDialog(dialog, _window);
                if (failure != null)
                    throw failure;

                var readonlyDialog = new ProjectExecutionWindow(store.GetAll().Single(item => item.StartedUtc == entry.StartedUtc), store);
                readonlyDialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        ((TabItem)readonlyDialog.FindName("ConfigurationTab")).IsSelected = true;
                        readonlyDialog.UpdateLayout();
                        check(FindLauncherVisuals<PasswordBox>((StackPanel)readonlyDialog.FindName("FieldsPanel")).Any(),
                            $"Persisted execution details always mask password inputs");
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { readonlyDialog.Close(); }
                };
                WpfDialogHost.ShowDialog(readonlyDialog, _window);
                if (failure != null)
                    throw failure;
        }
    }
    private static void QueueConfigurationName(Window owner, string name)
    {
        owner.Dispatcher.BeginInvoke(new Action(() =>
        {
            var editor = Application.Current.Windows.OfType<EditValueWindow>().Single();
            ((TextBox)editor.FindName("ValueTextBox")).Text = name;
            ((Button)editor.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }));
    }

}
