using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Runner.Serialization;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.Views;
using Newtonsoft.Json;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private void CheckExecutionCompletionViews(Action<bool, string> check, string directory)
    {
        foreach (var outcome in new[] { "running", "success", "failure", "missing-log" })
        {
            var location = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(location);
            var logfile = Path.Combine(location, "execution.log");
            if (outcome != "missing-log")
                File.WriteAllText(logfile, "Isolated launcher completion test.");
            var entry = new ProjectExecutionEntry
            {
                Id = Guid.NewGuid(),
                Project = new ProjectShortcut { Name = "Duplicate File Review" },
                StartedUtc = DateTime.UtcNow,
                ResponseFile = Path.Combine(location, "response.json")
            };
            var storePath = Path.Combine(location, "executions.json");
            File.WriteAllText(storePath, JsonConvert.SerializeObject(new[] { entry }));
            var error = "The input directory could not be read.";
            if (outcome != "running")
            {
                RunnerJson.WriteFile(entry.ResponseFile, new RunnerResponse
                {
                    Success = outcome == "success",
                    FinishedUtc = DateTime.UtcNow,
                    ErrorMessage = outcome == "success" ? null : error,
                    LogfilePath = logfile
                });
            }

            var dialog = new ProjectExecutionWindow(entry, new ProjectExecutions(storePath));
            Exception failure = null;
            dialog.ContentRendered += (_, _) =>
            {
                try
                {
                    var box = (Border)dialog.FindName("CompletionBox");
                    var loader = (ProgressBar)dialog.FindName("RunningIndicator");
                    if (outcome == "running")
                    {
                        check(box.Visibility == Visibility.Collapsed && loader.Visibility == Visibility.Visible,
                            "Running executions display progress without a completion message");
                    }
                    else
                    {
                        var title = (TextBlock)dialog.FindName("CompletionTitle");
                        var message = (TextBlock)dialog.FindName("CompletionMessage");
                        var expected = outcome == "success" ? nameof(Texts.Succeeded) : nameof(Texts.Failed);
                        check(box.Visibility == Visibility.Visible && loader.Visibility == Visibility.Collapsed
                            && title.Text == FlowBloxResourceUtil.GetLocalizedString(expected, typeof(Texts)),
                            $"The {outcome} response displays its localized completion box");
                        if (outcome != "success")
                            check(message.Text == error, "Failed execution details are read from the runner response file");
                        var link = (Hyperlink)dialog.FindName("LogLink");
                        var linkContainer = (TextBlock)dialog.FindName("LogLinkContainer");
                        check(linkContainer.Visibility == (outcome == "missing-log" ? Visibility.Collapsed : Visibility.Visible)
                            && (string)link.ToolTip == logfile,
                            "Completion log links target the response logfile and hide unavailable files");
                        if (outcome == "missing-log")
                            check(((TextBlock)dialog.FindName("LogUnavailableText")).Visibility == Visibility.Visible,
                                "An unavailable logfile is explained without exposing a broken link");
                    }
                    CaptureProjectCards(dialog, Path.Combine(directory, $"project-execution-{outcome}.png"));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    dialog.Close();
                }
            };
            WpfDialogHost.ShowDialog(dialog, _window);
            if (failure != null)
                throw failure;
        }
    }
}
