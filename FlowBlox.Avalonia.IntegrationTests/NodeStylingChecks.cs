using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;
using FlowBlox.Core.Models.FlowBlocks.Selection;
using FlowBlox.Core.Models.FlowBlocks.SequenceFlow;
using FlowBlox.Core.Models.FlowBlocks.TextOperations;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Runtime;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.ViewModels.ProjectPanel;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    public async Task<bool> RunNodeStylingAsync(string directory)
    {
        var checks = new List<string>();
        string error = null;
        try
        {
            var project = new FlowBloxProject { ProjectName = "FlowBlock styling" };
            FlowBloxProjectManager.Instance.ActiveProject = project;
            var projectPath = Path.Combine(directory, "node-styling.fbprj");
            FlowBloxProjectManager.Instance.ActiveProjectPath = projectPath;
            _window.SetProjectFile(projectPath);
            var registry = project.FlowBloxRegistry;
            var start = registry.CreateFlowBlockUnregistered<StartFlowBlock>();
            registry.Register(start);
            var names = new[] { "Normal", "Selected", "Reference", "Running", "Disabled", "Warning", "Error" };
            foreach (var name in names)
            {
                var block = new FormatFlowBlock { Name = name, FormatExpression = "Product {0}" };
                registry.PostProcessFlowBlockCreated(block);
                registry.Register(block);
                block.ReferencedFlowBlocks.Add(start);
            }

            _window.WindowState = global::Avalonia.Controls.WindowState.Maximized;
            _window.AfterProjectFullyInitialized();
            ProjectPanelView.OnAfterProjectOpened(project);
            _window.ActivatePanel(ProjectPanelView);
            _window.UpdateUI();
            await Task.Delay(350);
            var panel = (ProjectPanelWpfControl)ProjectPanelView.WpfContent;
            var nodes = panel.ViewModel.Nodes.Where(node => node.InternalFlowBlock is FormatFlowBlock).ToList();
            for (var index = 0; index < nodes.Count; index++)
            {
                nodes[index].X = 40 + index % 3 * 365;
                nodes[index].Y = 65 + index / 3 * 185;
            }
            var selected = nodes.Single(node => node.Name == "Selected");
            panel.UpdateLayout();
            var selectedBorder = GetStyledNodeBorder(panel, selected);
            var initialWidth = selectedBorder.ActualWidth;
            var initialHeight = selectedBorder.ActualHeight;
            var normal = GetStyledNodeBorder(panel, nodes.Single(node => node.Name == "Normal"));
            Require(normal.Background is LinearGradientBrush && normal.BorderBrush is LinearGradientBrush,
                "Normal nodes render a gradient surface and a persistent outline", checks);
            selected.IsSelected = true;
            nodes.Single(node => node.Name == "Reference").IsReference = true;
            var running = nodes.Single(node => node.Name == "Running");
            running.IsSelected = true;
            running.IsRuntimeFocused = true;
            var disabled = nodes.Single(node => node.Name == "Disabled");
            disabled.IsSelected = true;
            disabled.InternalFlowBlock.IsNotExecuted = true;
            disabled.InternalFlowBlock.BreakPoint = true;
            var runtime = new FlowBloxRuntime(project);
            var warning = nodes.Single(node => node.Name == "Warning");
            warning.IsSelected = true;
            warning.InternalFlowBlock.CreateNotification(runtime,
                SequenceDetectionFlowBlock.SequenceDetectionNotifications.ReturnedNoMatches);
            var failed = nodes.Single(node => node.Name == "Error");
            failed.IsSelected = true;
            failed.InternalFlowBlock.CreateNotification(runtime,
                SequenceDetectionFlowBlock.SequenceDetectionNotifications.NoSequencePatternSpecified);
            await Task.Delay(200);
            panel.UpdateLayout();
            Require(selectedBorder.ActualWidth == initialWidth && selectedBorder.ActualHeight == initialHeight,
                "Selection preserves node dimensions", checks);
            var selectionColor = ((SolidColorBrush)selectedBorder.BorderBrush).Color;
            var runtimeColor = ((SolidColorBrush)GetStyledNodeBorder(panel, running).BorderBrush).Color;
            Require(selectionColor != runtimeColor,
                "Runtime focus remains distinct when a node is selected", checks);
            var selectedHeader = MenuVisuals<Border>(selectedBorder)
                .Single(border => border.IsVisible && Equals(border.Tag, "FlowBlockHeader"));
            var runtimeHeader = MenuVisuals<Border>(GetStyledNodeBorder(panel, running))
                .Single(border => border.IsVisible && Equals(border.Tag, "FlowBlockHeader"));
            Require(ReferenceEquals(selectedHeader.Background, runtimeHeader.Background),
                "Selected headers remain visible beneath runtime outlines", checks);
            Require(warning.HasWarning && failed.HasError &&
                ((SolidColorBrush)GetStyledNodeBorder(panel, warning).BorderBrush).Color != selectionColor &&
                ((SolidColorBrush)GetStyledNodeBorder(panel, failed).BorderBrush).Color != runtimeColor,
                "Warning and error outlines remain visible on selected nodes", checks);
            SaveMenuImage(directory, panel, "node-styling-states.png");
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "smoke-result.json"), JsonSerializer.Serialize(
                new { Passed = error == null, Checks = checks, Error = error },
                new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }
        return error == null;
    }

    private static Border GetStyledNodeBorder(ProjectPanelWpfControl panel, FlowBlockNodeViewModel node)
    {
        var nodeStyle = panel.FindResource("ProjectPanel.BlockBorder");
        return MenuVisuals<Border>(panel).Single(border =>
            ReferenceEquals(border.DataContext, node) && ReferenceEquals(border.Style, nodeStyle));
    }
}
