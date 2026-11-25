using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FlowBlox.AppWindow.Contents;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider.Project;
using FlowBlox.UICore.Views;
using FlowBlox.Core.Util.Json;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    public async Task<bool> LayoutAndSaveAsync(string directory, string projectPath, bool autoLayout = true)
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
                if (control.IsLoaded && (!autoLayout || control.ViewModel.AutoLayoutCommand.CanExecute(null)))
                    break;
            }
            if (!control.IsLoaded || (autoLayout && !control.ViewModel.AutoLayoutCommand.CanExecute(null)))
                throw new InvalidOperationException($"The project panel is not ready for automatic layout: loaded={control.IsLoaded}, readOnly={control.ViewModel.IsProjectEditingReadOnly}, nodes={control.ViewModel.Nodes.Count}.");

            var originalDefinition = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(project, JsonSettings.ProjectExport()));
            NormalizeProjectReferenceIds(originalDefinition);

            if (autoLayout)
            {
                control.ViewModel.AutoLayoutCommand.Execute(null);
                await Task.Delay(300);
            }
            _window.itmSaveProject.Execute(null);
            await Task.Delay(300);
            var saved = FlowBloxProject.FromFile(workingPath);
            if (saved.FlowBlocks.Count != project.FlowBlocks.Count)
                throw new InvalidOperationException("Saved project did not retain all flow blocks.");
            if (!autoLayout)
            {
                var savedDefinition = JObject.Parse(Newtonsoft.Json.JsonConvert.SerializeObject(saved, JsonSettings.ProjectExport()));
                NormalizeProjectReferenceIds(savedDefinition);
                foreach (var key in new[] { "FlowBlocks", "ManagedObjects", "UserFields", "InputFiles", "ProjectDependendDataObjects" })
                {
                    if (!JToken.DeepEquals(originalDefinition[key], savedDefinition[key]))
                    {
                        File.WriteAllText(Path.Combine(directory, "before-save.json"), originalDefinition.ToString());
                        File.WriteAllText(Path.Combine(directory, "after-save.json"), savedDefinition.ToString());
                        throw new InvalidOperationException($"Saving without layout changed '{key}'.");
                    }
                }
            }
            if (!File.ReadAllBytes(projectPath).SequenceEqual(original))
                throw new InvalidOperationException("The source project changed while the layout session was open.");
            if (autoLayout)
            {
                File.Copy(workingPath, projectPath, true);
            }
            else
            {
                var originalNotice = JObject.Parse(System.Text.Encoding.UTF8.GetString(original).TrimStart('\uFEFF')).Value<string>("Notice");
                if (!string.Equals(saved.Notice, originalNotice, StringComparison.Ordinal))
                    throw new InvalidOperationException("The project notice changed during the application save.");
                // Keep the original definition: opening an older project may add inactive default rules.
                // The saved notice was verified above; no unrelated serialization changes are copied back.
            }
        }
        catch (Exception exception)
        {
            error = exception.ToString();
        }
        finally
        {
            File.WriteAllText(Path.Combine(directory, "layout-save-result.json"), JsonSerializer.Serialize(
                new { Passed = error == null, Project = projectPath, AutoLayout = autoLayout, Error = error },
                new JsonSerializerOptions { WriteIndented = true }));
            _window.Close();
        }

        return error == null;
    }

    private static void NormalizeProjectReferenceIds(JObject definition)
    {
        // Serialization may renumber references; compare their object paths instead of transient IDs.
        var objects = definition.DescendantsAndSelf().OfType<JObject>().ToList();
        var paths = objects.Where(item => item["$id"] != null)
            .ToDictionary(item => item.Value<string>("$id"), item => item.Path);
        foreach (var item in objects)
        {
            foreach (var key in new[] { "$id", "$ref" })
            {
                var id = item.Value<string>(key);
                if (id != null && paths.TryGetValue(id, out var path))
                    item[key] = path;
            }
        }
    }
}
