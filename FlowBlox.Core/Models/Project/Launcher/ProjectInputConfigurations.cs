using FlowBlox.Core.Constants;

namespace FlowBlox.Core.Models.Project.Launcher;

/// <summary>
/// Explicitly saved, named input presets shared by the desktop and web dashboards.
/// </summary>
public sealed class ProjectInputConfigurations
{
    public static ProjectInputConfigurations Instance { get; } = new(
        Path.Combine(Path.GetDirectoryName(GlobalPaths.RecentProjectsPath)!, "fbinputconfigurations.json"));

    private readonly LauncherJsonStore<ProjectInputConfiguration> _store;

    public ProjectInputConfigurations(string path)
    {
        _store = new LauncherJsonStore<ProjectInputConfiguration>(path);
    }

    public IReadOnlyList<ProjectInputConfiguration> GetForProject(ProjectShortcut project) => _store.Read()
        .Where(item => ProjectShortcutIdentity.Matches(item.Project, GetIdentity(project)))
        .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    public void Save(ProjectInputConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuration.Name);
        configuration.Name = configuration.Name.Trim();
        configuration.Project = GetIdentity(configuration.Project);
        _store.Update(items =>
        {
            items.RemoveAll(item => item.Id == configuration.Id);
            items.Add(configuration);
        });
    }

    public void Rename(ProjectShortcut project, Guid id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _store.Update(items =>
        {
            var configuration = items.Single(item => item.Id == id && ProjectShortcutIdentity.Matches(item.Project, GetIdentity(project)));
            configuration.Name = name.Trim();
        });
    }

    private static ProjectShortcut GetIdentity(ProjectShortcut project) => new()
    {
        Name = project.Name,
        ProjectFile = project.ProjectFile,
        ProjectSpaceGuid = project.ProjectSpaceGuid,
        ApiEndpoint = string.IsNullOrWhiteSpace(project.ApiEndpoint) ? ProjectShortcut.CurrentApiEndpoint : project.ApiEndpoint
    };
}
