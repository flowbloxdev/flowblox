using FlowBlox.Core.Constants;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util.Resources;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.Project.Launcher;

/// <summary>
/// Named presets and a read-only local developer profile shared by desktop and web dashboards.
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

    public IReadOnlyList<ProjectInputConfiguration> GetForProject(ProjectShortcut project)
    {
        var configurations = _store.Read()
            .Where(item => ProjectShortcutIdentity.Matches(item.Project, GetIdentity(project)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var local = ReadLocalConfiguration(project);
        if (local != null)
            configurations.Insert(0, local);

        return configurations;
    }

    public void Save(ProjectInputConfiguration configuration)
    {
        EnsureWritable(configuration.Id);
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
        EnsureWritable(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _store.Update(items =>
        {
            var configuration = items.Single(item => item.Id == id && ProjectShortcutIdentity.Matches(item.Project, GetIdentity(project)));
            configuration.Name = name.Trim();
        });
    }

    private static ProjectInputConfiguration? ReadLocalConfiguration(ProjectShortcut project)
    {
        if (!File.Exists(project.ProjectFile))
            return null;

        var path = Path.ChangeExtension(project.ProjectFile, ".fblocaldata");
        if (!File.Exists(path))
            return null;

        var local = JsonConvert.DeserializeObject<FlowBloxProjectLocalData>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Invalid local project data: {path}");
        if (local.LocalUserFieldValues == null
            || !local.LocalUserFieldValues.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            return null;

        return new ProjectInputConfiguration
        {
            Id = Guid.Empty,
            Name = FlowBloxResourceUtil.GetLocalizedString("ProjectInputConfiguration_LocalDeveloper", typeof(FlowBloxTexts)),
            Project = GetIdentity(project),
            Request = new RunnerRequest
            {
                UserFields = new(local.LocalUserFieldValues ?? new(), StringComparer.OrdinalIgnoreCase)
            }
        };
    }

    private static void EnsureWritable(Guid id)
    {
        if (id == Guid.Empty)
            throw new InvalidOperationException(FlowBloxResourceUtil.GetLocalizedString(
                "ProjectInputConfiguration_ReadOnly", typeof(FlowBloxTexts)));
    }

    private static ProjectShortcut GetIdentity(ProjectShortcut project) => new()
    {
        Name = project.Name,
        ProjectFile = project.ProjectFile,
        ProjectSpaceGuid = project.ProjectSpaceGuid,
        ApiEndpoint = string.IsNullOrWhiteSpace(project.ApiEndpoint) ? ProjectShortcut.CurrentApiEndpoint : project.ApiEndpoint
    };
}
