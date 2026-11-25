using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using FlowBlox.AppWindow.Contents;
using FlowBlox.AIAssistant.Models;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Util;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    public async Task<bool> RunAssistantProjectLiveAsync(string directory)
    {
        var checks = new List<string>();
        string error = null;
        var historyOption = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.AIAssistantHistoryDirectory);
        var previousHistoryDirectory = historyOption.Value;
        var approvalTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        try
        {
            var url = Environment.GetEnvironmentVariable("FLOWBLOX_ASSISTANT_URL");
            var prompt = Environment.GetEnvironmentVariable("FLOWBLOX_ASSISTANT_PROMPT");
            var expectedPattern = Environment.GetEnvironmentVariable("FLOWBLOX_ASSISTANT_EXPECTED_PATTERN");
            var output = Environment.GetEnvironmentVariable("FLOWBLOX_ASSISTANT_OUTPUT");
            Require(!string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(prompt) &&
                !string.IsNullOrWhiteSpace(expectedPattern) && !string.IsNullOrWhiteSpace(output),
                "Caller supplies the live task and an independent extraction pattern", checks);
            Require(!File.Exists(output), "The output file does not exist before the assistant run", checks);
            historyOption.Value = Path.Combine(directory, "assistant-history");
            var projectPath = Path.Combine(directory, "assistant-created.fbprj");
            var project = new FlowBloxProject { ProjectName = "Assistant Live Verification" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            FlowBloxProjectManager.Instance.ActiveProjectPath = projectPath;
            _window.SetProjectFile(projectPath);
            _window.AfterProjectFullyInitialized();
            ProjectPanelView.OnAfterProjectOpened(project);
            var panel = _window.Panels().OfType<AIAssistantView>().Single();
            _window.ActivatePanel(panel);
            _window.UpdateUI();
            await Task.Delay(300);
            var control = (AIAssistantControl)panel.WpfContent;
            var model = (AIAssistantViewModel)control.DataContext;
            model.NewChatCommand.Execute(null);
            model.ChatViewModel.CurrentInput = prompt;
            Require(model.ChatViewModel.SubmitCommand.CanExecute(null), "The real assistant UI accepts the task", checks);
            approvalTimer.Tick += (_, _) => ApproveRequestedResearch(url);
            approvalTimer.Start();
            model.ChatViewModel.SubmitCommand.Execute(null);
            var deadline = DateTime.UtcNow.AddMinutes(12);
            while (model.ChatViewModel.IsBusy && DateTime.UtcNow < deadline)
            {
                await Task.Delay(500);
                File.WriteAllLines(Path.Combine(directory, "assistant-transcript.txt"),
                    model.ChatViewModel.Transcript.Select(x => x.Kind + ": " + x.Text));
            }
            Require(!model.ChatViewModel.IsBusy, "The assistant completed within the deadline", checks);
            SaveMenuImage(directory, control, "assistant-cost-badge.png");
            using var debug = model.ChatViewModel.CreateDebugViewModel();
            debug.SelectedTopic = debug.Topics.Single(x => x.Topic == AiAssistantDebugTopic.Usage);
            File.WriteAllText(Path.Combine(directory, "session-usage.txt"), debug.SelectedCard.Content);
            var debugView = new AiAssistantDebugView(debug);
            debugView.Show();
            await Task.Delay(300);
            debugView.UpdateLayout();
            SaveMenuImage(directory, debugView, "assistant-session-usage.png");
            debugView.Close();

            _window.ActivatePanel(ProjectPanelView);
            var projectControl = (ProjectPanelWpfControl)ProjectPanelView.WpfContent;
            projectControl.ViewModel.AutoLayoutCommand.Execute(null);
            _window.itmSaveProject.Execute(null);
            await Task.Delay(250);
            Require(File.Exists(projectPath), "The project is laid out and saved through the application", checks);
            Require(!model.ChatViewModel.Transcript.Any(x => x.Kind == AssistantTranscriptKind.Error),
                "The assistant completed without a terminal error", checks);
            Require(model.ChatViewModel.EstimatedCostText.Contains("$", StringComparison.Ordinal),
                "The cost badge displays priced session usage", checks);
            Require(File.Exists(output), "The assistant produced the requested workbook", checks);
            using var client = new HttpClient();
            var html = await client.GetStringAsync(url);
            var expected = Regex.Matches(html, expectedPattern, RegexOptions.Singleline, TimeSpan.FromSeconds(5))
                .Select(x => WebUtility.HtmlDecode(x.Groups[1].Value).Trim()).ToArray();
            Require(expected.Length > 0, "Independent extraction found expected values", checks);
            var cells = ReadWorkbookCells(output);
            Require(cells.Skip(1).SequenceEqual(expected), "The workbook contains exactly the independently extracted values after its header", checks);
            File.WriteAllText(Path.Combine(directory, "extraction-result.json"), JsonSerializer.Serialize(new { Expected = expected, Cells = cells }));
            var beforeReset = debug.ContextStatistics;
            model.ChatViewModel.ResetSessionUsageCommand.Execute(null);
            Require(model.ChatViewModel.Transcript.Count > 0, "Reset preserves the conversation", checks);
            using var afterReset = model.ChatViewModel.CreateDebugViewModel();
            Require(!model.ChatViewModel.HasSessionUsage, "Reset clears cumulative session usage", checks);
            Require(afterReset.ContextStatistics == beforeReset, "Reset preserves context occupancy", checks);
        }
        catch (Exception ex)
        {
            error = ex.ToString();
        }
        finally
        {
            approvalTimer.Stop();
            historyOption.Value = previousHistoryDirectory;
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(
                new { Passed = error == null, Checks = checks, Error = error }, new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }
        return error == null;
    }

    private static string[] ReadWorkbookCells(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var shared = new List<string>();
        var strings = archive.GetEntry("xl/sharedStrings.xml");
        if (strings != null)
        {
            using var stream = strings.Open();
            shared.AddRange(XDocument.Load(stream).Descendants(spreadsheet + "si")
                .Select(x => string.Concat(x.Descendants(spreadsheet + "t").Select(t => t.Value))));
        }
        var values = new List<string>();
        foreach (var sheet in archive.Entries.Where(x => x.FullName.StartsWith("xl/worksheets/sheet", StringComparison.Ordinal) && x.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var stream = sheet.Open();
            foreach (var cell in XDocument.Load(stream).Descendants(spreadsheet + "c"))
            {
                var value = cell.Element(spreadsheet + "v")?.Value ?? string.Concat(cell.Descendants(spreadsheet + "t").Select(x => x.Value));
                values.Add((string)cell.Attribute("t") == "s" ? shared[int.Parse(value)] : value);
            }
        }
        return values.ToArray();
    }
}
