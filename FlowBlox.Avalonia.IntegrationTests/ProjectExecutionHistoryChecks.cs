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
                        DefaultValue = name == "Secret" ? null : "project default"
                    };
                    project.FlowBloxRegistry.Register(field);
                    project.UserFields.Add(field);
                }
                var dialog = new ProjectExecutionWindow(shortcut, project, store);
                Exception failure = null;
                dialog.ContentRendered += (_, _) =>
                {
                    try
                    {
                        var history = (ListBox)dialog.FindName("HistoryList");
                        var fields = (StackPanel)dialog.FindName("FieldsPanel");
                        check(history.Items.Count == 2 && history.SelectedIndex == 0,
                            "Execution history contains only this project's snapshots and selects the latest");
                        check(project.UserFields.Single(field => field.Name == "Count").StringValue == "last input"
                            && project.UserFields.Single(field => field.Name == "Added").StringValue == "project default",
                            "Latest snapshot restores matching inputs while new inputs retain current project defaults");
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
                            "Execution snapshot restores its option overrides");
                        history.SelectedIndex = 1;
                        check(project.UserFields.Single(field => field.Name == "Count").StringValue == "older input",
                            "Selecting an older execution replaces the current parameter values");
                        ((Button)dialog.FindName("ClearInputsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        dialog.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                        check(passwordEditor.Text == string.Empty,
                            "Clearing input history also clears the revealed password editor");
                        check(history.SelectedIndex == -1
                            && project.UserFields.Single(field => field.Name == "Count").StringValue == "project default"
                            && string.IsNullOrEmpty(project.UserFields.Single(field => field.Name == "Secret").StringValue)
                            && ((DataGrid)dialog.FindName("OptionsGrid")).Items.Count == 0,
                            "Clear restores original inputs, clears snapshot selection and removes overrides");
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
}
