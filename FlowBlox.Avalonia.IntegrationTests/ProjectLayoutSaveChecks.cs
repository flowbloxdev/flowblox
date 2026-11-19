using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowBlox.AppWindow.Contents;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.Views;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    public async Task<bool> LayoutAndSaveAsync(string directory, string projectPath)
    {
        string error = null;
        try
        {
            var original = File.ReadAllBytes(projectPath);
            var workingPath = Path.Combine(directory, Path.GetFileName(projectPath));
            File.Copy(projectPath, workingPath, true);
            foreach (var extension in new[] { ".fbdeps", ".fblocaldata" })
            {
                var sidecar = Path.ChangeExtension(projectPath, extension);
                if (File.Exists(sidecar))
                    File.Copy(sidecar, Path.ChangeExtension(workingPath, extension), true);
            }
            var project = FlowBloxProject.FromFile(workingPath);
            _window.SetProjectFile(workingPath);
            FlowBloxProjectManager.Instance.ActiveProjectPath = workingPath;
            FlowBloxProjectManager.Instance.ActiveProject = project;
            _window.AfterProjectFullyInitialized();
            ProjectPanelView.OnAfterProjectOpened(project);
            _window.ActivatePanel(ProjectPanelView);
            _window.UpdateUI();
            _window.UpdateLayout();
            var control = (ProjectPanelWpfControl)ProjectPanelView.WpfContent;
            for (var attempt = 0; attempt < 50; attempt++)
            {
                _window.UpdateLayout();
                await Task.Delay(100);
                if (control.IsLoaded && control.ViewModel.AutoLayoutCommand.CanExecute(null))
                    break;
            }
            if (!control.IsLoaded || !control.ViewModel.AutoLayoutCommand.CanExecute(null))
                throw new InvalidOperationException($"The project panel is not ready for automatic layout: loaded={control.IsLoaded}, readOnly={control.ViewModel.IsProjectEditingReadOnly}, nodes={control.ViewModel.Nodes.Count}.");

            control.ViewModel.AutoLayoutCommand.Execute(null);
            await Task.Delay(300);
            _window.itmSaveProject.Execute(null);
            await Task.Delay(300);
            var saved = FlowBloxProject.FromFile(workingPath);
            if (saved.FlowBlocks.Count != project.FlowBlocks.Count)
                throw new InvalidOperationException("Saved project did not retain all flow blocks.");
            if (!File.ReadAllBytes(projectPath).SequenceEqual(original))
                throw new InvalidOperationException("The source project changed while the layout session was open.");
            File.Copy(workingPath, projectPath, true);
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "layout-save-result.json"), JsonSerializer.Serialize(
                new { Passed = error == null, Project = projectPath, Error = error },
                new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }

        return error == null;
    }
}
