using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Tools;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.Web;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.ViewModels.PropertyView;
using FlowBlox.UICore.Views;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    public async Task<bool> RunComponentAssistantAsync(string directory, bool live)
    {
        var results = new List<string>();
        var timings = new Dictionary<string, double>();
        string error = null;
        PropertyWindow editor = null;
        var historyOption = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantHistoryDirectory);
        var previousHistoryDirectory = historyOption.Value;
        try
        {
            historyOption.Value = Path.Combine(directory, "assistant-history");
            var projectPath = Path.Combine(directory, "component-assistant.fbprj");
            var project = new FlowBloxProject { ProjectName = "Component Assistant" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            FlowBloxProjectManager.Instance.ActiveProjectPath = projectPath;
            _window.SetProjectFile(projectPath);
            var registry = project.FlowBloxRegistry;
            var start = registry.CreateFlowBlockUnregistered<StartFlowBlock>();
            registry.PostProcessFlowBlockCreated(start);
            registry.Register(start);
            var request = registry.CreateFlowBlockUnregistered<WebRequestFlowBlock>();
            registry.PostProcessFlowBlockCreated(request);
            registry.Register(request);
            request.Url = live ? Environment.GetEnvironmentVariable("FLOWBLOX_COMPONENT_ASSISTANT_URL") : "https://example.invalid/catalogue";
            var block = registry.CreateFlowBlockUnregistered<RegexSelectorFlowBlock>();
            registry.PostProcessFlowBlockCreated(block);
            registry.Register(block);
            start.ReferencedFlowBlocks.Add(request);
            request.ReferencedFlowBlocks.Add(block);
            block.InputField = request.Fields.First();
            block.RegularExpression = "Before";
            _window.AfterProjectFullyInitialized();
            ProjectPanelView.OnAfterProjectOpened(project);
            _window.ActivatePanel(ProjectPanelView);
            _window.UpdateUI();
            await Task.Delay(350);

            var timer = Stopwatch.StartNew();
            editor = new PropertyWindow(new PropertyWindowArgs(block)) { ShowInTaskbar = false };
            timings["propertyWindowConstructionMs"] = timer.Elapsed.TotalMilliseconds;
            var editorChecks = CheckComponentEditorAsync(editor, block, request, directory, live, results, timings);
            editor.ShowDialog();
            await editorChecks;
            editor = null;

            var projectControl = (ProjectPanelWpfControl)ProjectPanelView.WpfContent;
            projectControl.ViewModel.AutoLayoutCommand.Execute(null);
            _window.itmSaveProject.Execute(null);
            await Task.Delay(250);
            Require(File.Exists(projectPath), "The sample flow is laid out and saved through the active application", results);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            if (editor != null)
                SaveMenuImage(directory, editor, "component-assistant-failure.png");
        }
        finally
        {
            editor?.Close();
            historyOption.Value = previousHistoryDirectory;
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(
                new { Passed = error == null, Checks = results, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
            File.WriteAllText(Path.Combine(directory, "assistant-opening-timings.json"), JsonSerializer.Serialize(
                timings, new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }
        return error == null;
    }

    private async Task CheckComponentEditorAsync(
        PropertyWindow editor,
        RegexSelectorFlowBlock block,
        WebRequestFlowBlock request,
        string directory,
        bool live,
        List<string> results,
        Dictionary<string, double> timings)
    {
        try
        {
            await Task.Delay(300);
            var properties = ((PropertyWindowViewModel)editor.DataContext).PropertyViewModel;
            var working = (RegexSelectorFlowBlock)properties.WorkingCopy;
            var assistant = (ComponentAssistantView)editor.FindName("ComponentAssistant");
            var toggle = (ToggleButton)assistant.FindName("AssistantToggle");
            var collapsedWidth = editor.ActualWidth;
            var timer = Stopwatch.StartNew();
            toggle.IsChecked = true;
            timings["firstOpenSynchronousMs"] = timer.Elapsed.TotalMilliseconds;
            await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ContextIdle);
            timings["firstOpenUntilIdleMs"] = timer.Elapsed.TotalMilliseconds;
            var control = MenuVisuals<AIAssistantControl>(assistant).Single();
            var model = (AIAssistantViewModel)control.DataContext;
            Require(model.IsComponentMode && model.IsChatVisible && editor.ActualWidth > collapsedWidth,
                "Opening the rail expands the editor and starts a component chat", results);
            SaveMenuImage(directory, editor, "component-assistant-expanded.png");
            toggle.IsChecked = false;
            await Task.Delay(100);
            Require(Math.Abs(editor.ActualWidth - collapsedWidth) < 2, "Collapsing restores the editor width", results);
            timer.Restart();
            toggle.IsChecked = true;
            await editor.Dispatcher.InvokeAsync(editor.UpdateLayout, System.Windows.Threading.DispatcherPriority.ContextIdle);
            timings["reopenUntilIdleMs"] = timer.Elapsed.TotalMilliseconds;
            Require(ReferenceEquals(control, MenuVisuals<AIAssistantControl>(assistant).Single()),
                "Expanding again retains the same conversation", results);

            if (live)
            {
                var prompt = Environment.GetEnvironmentVariable("FLOWBLOX_COMPONENT_ASSISTANT_PROMPT");
                Require(!string.IsNullOrWhiteSpace(prompt) && !string.IsNullOrWhiteSpace(request.Url),
                    "Live test receives its URL and task from the caller", results);
                var config = model.GetConfiguration(out var configError);
                Require(string.IsNullOrWhiteSpace(configError) && config.SelectedProvider != null,
                    "The existing assistant provider is configured", results);
                File.WriteAllText(Path.Combine(directory, "provider.txt"), config.SelectedProvider.GetType().Name);
                model.ChatViewModel.CurrentInput = prompt;
                Require(model.ChatViewModel.SubmitCommand.CanExecute(null), "Component chat accepts the live request", results);
                model.ChatViewModel.SubmitCommand.Execute(null);
                var approvalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                approvalTimer.Tick += (_, _) => ApproveRequestedResearch(request.Url);
                approvalTimer.Start();
                var deadline = DateTime.UtcNow.AddMinutes(8);
                while (model.ChatViewModel.IsBusy && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(500);
                    File.WriteAllLines(Path.Combine(directory, "assistant-transcript.txt"), model.ChatViewModel.Transcript.Select(line => line.Text));
                }
                approvalTimer.Stop();
                Require(!model.ChatViewModel.IsBusy, "The live assistant completed within the deadline", results);
                Require(working.RegularExpression != "Before" && !string.IsNullOrWhiteSpace(working.RegularExpression),
                    "The assistant configured the regex on the working copy", results);
                using var client = new HttpClient();
                var html = await client.GetStringAsync(request.Url);
                var options = RegexOptions.None;
                var matches = Regex.Matches(html, working.RegularExpression, options, TimeSpan.FromSeconds(5));
                var values = matches.Select(match => WebUtility.HtmlDecode(
                    string.IsNullOrEmpty(working.Group) ? match.Value : match.Groups[working.Group].Value)).ToArray();
                var expectedPattern = Environment.GetEnvironmentVariable("FLOWBLOX_COMPONENT_ASSISTANT_EXPECTED_PATTERN");
                Require(!string.IsNullOrWhiteSpace(expectedPattern), "The caller supplied an independent expected extraction", results);
                var expected = Regex.Matches(html, expectedPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5))
                    .Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();
                Require(expected.Length > 0 && values.SequenceEqual(expected),
                    "The generated regex extracts exactly the expected values from the live page", results);
                File.WriteAllText(Path.Combine(directory, "regex-result.json"), JsonSerializer.Serialize(
                    new { working.RegularExpression, working.Group, Values = values }, new JsonSerializerOptions { WriteIndented = true }));
                SaveMenuImage(directory, editor, "component-assistant-result.png");
            }
            else
            {
                var api = new DefaultToolApi { ComponentContext = model.ChatViewModel.ComponentContext };
                var response = await api.ExecuteAsync(new ToolRequest
                {
                    ToolName = "UpdateFlowBlock",
                    Arguments = new JObject { ["name"] = working.Name, ["path"] = "/RegularExpression", ["value"] = "(?<title>Sample)" }
                }, CancellationToken.None);
                Require(response.Ok && properties.IsDirty && working.RegularExpression == "(?<title>Sample)" && block.RegularExpression == "Before",
                    "Assistant tool updates refresh the property editor and remain local", results);
            }

            Require(block.RegularExpression == "Before", "The original remains unchanged before editor commit", results);
            model.BackToHistoryCommand.Execute(null);
            Require(model.IsHistoryOverviewVisible, "The component assistant exposes the history overview", results);
            model.NewChatCommand.Execute(null);
            Require(model.IsChatVisible && model.ChatViewModel.Transcript.Count == 0,
                "New Chat starts a fresh component conversation", results);
            if (live)
            {
                Require(await properties.SaveAsync(editor, true), "The editor commits the generated configuration", results);
                Require(block.RegularExpression == working.RegularExpression, "Commit updates the original block", results);
            }
            editor.Close();
            if (!live)
                Require(block.RegularExpression == "Before", "Closing without commit discards assistant changes", results);

        }
        finally
        {
            editor.Close();
        }
    }

    private static void ApproveRequestedResearch(string url)
    {
        var dialogs = Application.Current.Windows.OfType<ExternalProjectExecutionConfirmationWindow>().ToList();
        foreach (var dialog in dialogs)
        {
            var grid = (System.Windows.Controls.DataGrid)dialog.FindName("InputParametersGrid");
            if (grid.ItemsSource is IEnumerable<KeyValuePair<string, string>> parameters &&
                parameters.Any(parameter => parameter.Value.Contains(url, StringComparison.Ordinal)))
            {
                var allow = (System.Windows.Controls.Button)dialog.FindName("AllowOnceButton");
                allow.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            }
        }
    }

    private static void Require(bool condition, string name, ICollection<string> results)
    {
        if (!condition)
            throw new InvalidOperationException(name);
        results.Add(name);
    }
}
