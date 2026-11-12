using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Interop;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.AvaloniaDesktop.Persistence.RecentProjects;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.DistributedData;
using FlowBlox.UICore.Commands;
using FlowBlox.UICore.Views;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    public ObservableCollection<ProjectShortcutSection> ShortcutSections { get; } = new();
    private readonly DispatcherTimer _shortcutTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _refreshingShortcuts;
    private bool _shortcutsClosed;

    private void InitializeShortcuts()
    {
        this.FindControl<ItemsControl>("ShortcutBoxes").ItemsSource = ShortcutSections;
        _shortcutTimer.Tick += ShortcutTimer_Tick;
        ProjectSpaceFavorites.Instance.Changed += Favorites_Changed;
        Activated += Shortcuts_Activated;
        _shortcutTimer.Start();
        _ = RefreshShortcutsAsync();
    }

    private void DisposeShortcuts()
    {
        _shortcutsClosed = true;
        _shortcutTimer.Stop();
        _shortcutTimer.Tick -= ShortcutTimer_Tick;
        ProjectSpaceFavorites.Instance.Changed -= Favorites_Changed;
        Activated -= Shortcuts_Activated;
    }

    private void Favorites_Changed(object sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => _ = RefreshShortcutsAsync());

    private void Shortcuts_Activated(object sender, EventArgs e) => _ = RefreshShortcutsAsync();

    private async void ShortcutTimer_Tick(object sender, EventArgs e)
    {
        if (_refreshingShortcuts || _shortcutsClosed)
            return;
        try
        {
            if (await Task.Run(() => ProjectExecutions.Instance.Refresh()))
                await RefreshShortcutsAsync();
        }
        catch (Exception exception)
        {
            _shortcutTimer.Stop();
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to refresh launcher executions.", exception);
        }
    }

    private async Task RefreshShortcutsAsync()
    {
        if (_refreshingShortcuts || _shortcutsClosed)
            return;
        _refreshingShortcuts = true;
        _ownProjectsRefreshVersion++;
        try
        {
            var sections = await Task.Run(BuildShortcutSections);
            if (_shortcutsClosed)
                return;
            ShortcutSections.Clear();
            foreach (var section in sections)
                ShortcutSections.Add(section);
            _ = RefreshOwnProjectShortcutsAsync();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to load project launcher shortcuts.", exception);
        }
        finally
        {
            _refreshingShortcuts = false;
        }
    }

    private List<ProjectShortcutSection> BuildShortcutSections()
    {
        var sections = new List<ProjectShortcutSection>();
        var projectDirectory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsProjectDir)?.Value;
        var localFiles = Directory.Exists(projectDirectory)
            ? Directory.EnumerateFiles(projectDirectory, "*.fbprj", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            })
            : Enumerable.Empty<string>();
        AddShortcutSection(sections, Texts.LocalProjects, ReadShortcutFiles(localFiles));

        var recent = RecentProjectsManager.Instance.GetRecentProjects().Select(x => new ProjectShortcut
        {
            Name = string.IsNullOrWhiteSpace(x.ProjectName) ? Path.GetFileNameWithoutExtension(x.ProjectFilePath) : x.ProjectName,
            ProjectFile = x.ProjectFilePath,
            ProjectSpaceGuid = x.ProjectSpaceGuid,
            ApiEndpoint = x.ApiEndpoint
        });
        AddShortcutSection(sections, Texts.RecentProjects, recent);

        var examples = DistributedDataCatalog.GetDataTypes()
            .FirstOrDefault(x => string.Equals(x.Name, DistributedDataCatalog.ExampleProjectType, StringComparison.OrdinalIgnoreCase));
        if (examples != null)
            AddShortcutSection(sections, Texts.Examples, ReadShortcutFiles(examples.FileNames
                .Select(x => DistributedDataCatalog.ResolvePath(examples.Name, x))));

        try
        {
            AddShortcutSection(sections, Texts.Favorites, ProjectSpaceFavorites.Instance.GetAll());
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to read launcher favorites.", exception);
        }
        try
        {
            var executions = ProjectExecutions.Instance.GetAll().Select(x => CreateShortcutItem(x.Project, x)).ToList();
            AddShortcutSection(sections, Texts.Executions, executions);
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to read launcher executions.", exception);
        }
        return sections;
    }

    private static IReadOnlyList<ProjectShortcut> ReadShortcutFiles(IEnumerable<string> files)
    {
        var shortcuts = new List<ProjectShortcut>();
        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;
            try
            {
                shortcuts.Add(ProjectShortcut.FromFile(file));
            }
            catch (Exception exception)
            {
                FlowBloxLogManager.Instance.GetLogger().Warn($"Failed to read launcher project metadata from '{file}'.", exception);
            }
        }
        return shortcuts.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void AddShortcutSection(List<ProjectShortcutSection> sections, string title, IEnumerable<ProjectShortcut> projects) =>
        AddShortcutSection(sections, title, projects.Select(x => CreateShortcutItem(x)).ToList());

    private static void AddShortcutSection(List<ProjectShortcutSection> sections, string title, List<ProjectShortcutItem> items)
    {
        if (items.Count == 0)
            return;
        var iconWidth = items.Any(x => !x.IsExecution && x.IsLocal && x.IsOnline) ? 36 : 16;
        foreach (var item in items)
            item.IconWidth = iconWidth;
        sections.Add(new ProjectShortcutSection { Title = title, Items = items });
    }

    private ProjectShortcutItem CreateShortcutItem(ProjectShortcut project, ProjectExecutionEntry execution = null)
    {
        var status = execution == null ? string.Empty
            : execution.IsRunning ? Texts.Running : execution.Response?.Success == true ? Texts.Succeeded : Texts.Failed;
        var item = new ProjectShortcutItem
        {
            Project = project,
            Execution = execution,
            Detail = execution == null ? string.Join(Environment.NewLine, project.Name, project.ProjectFile, project.ApiEndpoint) :
                $"{status} · {execution.StartedUtc.ToLocalTime():g}",
            OpenText = execution == null ? Texts.Open : FlowBlox.Core.Util.Resources.FlowBloxResourceUtil.GetLocalizedString(
                nameof(Texts.ViewExecution), typeof(Texts)),
            RunText = Texts.Execute,
            RemoveText = Texts.Remove
        };
        item.OpenCommand = new RelayCommand(() => _ = OpenShortcutAsync(item, execute: false));
        item.RunCommand = new RelayCommand(() => _ = OpenShortcutAsync(item, execute: true));
        item.RemoveCommand = new RelayCommand(() => RemoveExecution(item));
        return item;
    }

    private async Task OpenShortcutAsync(ProjectShortcutItem item, bool execute)
    {
        try
        {
            if (item.Execution != null)
                WpfDialogHost.ShowDialog(new ProjectExecutionWindow(item.Execution), this);
            else if (execute)
            {
                var project = await item.Project.LoadAsync();
                WpfDialogHost.ShowDialog(new ProjectExecutionWindow(item.Project, project), this);
            }
            else
            {
                await OpenRecentProjectAsync(new RecentProjectEntry
                {
                    ProjectName = item.Project.Name,
                    ProjectFilePath = item.Project.ProjectFile,
                    ProjectSpaceGuid = item.Project.ProjectSpaceGuid,
                    ApiEndpoint = item.Project.ApiEndpoint
                });
            }
        }
        catch (FlowBlox.Core.Exceptions.ProjectSpaceLoginRequiredException)
        {
            await ShowProjectSpaceLoginHintAsync();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to open a project launcher item.", exception);
            DesktopDialogs.Messages.Show(string.Format(Texts.Error, exception.Message), Texts.Title,
                MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
        await RefreshShortcutsAsync();
    }

    private void RemoveExecution(ProjectShortcutItem item)
    {
        try
        {
            ProjectExecutions.Instance.Remove(item.Execution.Id);
            _ = RefreshShortcutsAsync();
        }
        catch (Exception exception)
        {
            FlowBloxLogManager.Instance.GetLogger().Error("Failed to remove a launcher execution.", exception);
        }
    }
}
