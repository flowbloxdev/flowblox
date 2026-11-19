using System.Security.Cryptography;
using System.Text;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Enums;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Models.Components;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Models.Project.Launcher;
using FlowBlox.Core.Provider;
using FlowBlox.Core.Provider.Project;
using FlowBlox.Core.Runner.Contracts;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Projects;
using FlowBlox.Core.Util.Vault;
using FlowBlox.WebServer.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.WebServer.Services;

/// <summary>Shares the desktop catalog and isolated runner contract; never accepts a client-supplied project path.</summary>
public sealed class DashboardService(ILogger<DashboardService> logger)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<WebProject> _projects = [];
    public IReadOnlyList<WebProject> Projects => _projects;
    public ProjectExecutions Executions => ProjectExecutions.Instance;
    public ProjectInputConfigurations InputConfigurations => ProjectInputConfigurations.Instance;

    public async Task RefreshProjectsAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var projects = new List<WebProject>();
            var directory = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.PathsProjectDir)?.Value;
            if (Directory.Exists(directory))
            {
                var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
                foreach (var file in Directory.EnumerateFiles(directory, "*.fbprj", options))
                {
                    try { AddProject(projects, ProjectShortcut.FromFile(file), "Local"); }
                    catch (Exception exception) { logger.LogWarning(exception, "Could not read local project metadata."); }
                }
            }
            if (File.Exists(GlobalPaths.RecentProjectsPath))
            {
                foreach (var item in JArray.Parse(await File.ReadAllTextAsync(GlobalPaths.RecentProjectsPath)))
                {
                    var shortcut = new ProjectShortcut
                    {
                        Name = item.Value<string>("ProjectName") ?? string.Empty,
                        ProjectFile = item.Value<string>("ProjectFilePath") ?? string.Empty,
                        ProjectSpaceGuid = item.Value<string>("ProjectSpaceGuid") ?? string.Empty,
                        ApiEndpoint = item.Value<string>("ApiEndpoint") ?? string.Empty
                    };
                    if (!string.IsNullOrEmpty(shortcut.ProjectFile) && !File.Exists(shortcut.ProjectFile))
                        continue;
                    AddProject(projects, shortcut, "Loaded");
                }
            }
            foreach (var shortcut in ProjectSpaceFavorites.Instance.GetAll())
                AddProject(projects, shortcut, "ProjectSpace");
            _projects = projects.OrderBy(project => project.Shortcut.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            try
            {
                var endpoint = ProjectShortcut.CurrentApiEndpoint;
                var token = FlowBloxAccountManager.Instance.GetUserToken(endpoint);
                if (!string.IsNullOrEmpty(token))
                {
                    var response = await new FlowBloxWebApiService(endpoint).GetProjectsAsync(userToken: token, mine: true)
                        .WaitAsync(TimeSpan.FromSeconds(15));
                    if (response.Success && response.ResultObject != null)
                        foreach (var project in response.ResultObject)
                            AddProject(projects, new ProjectShortcut { Name = project.Name, ProjectSpaceGuid = project.Guid, ApiEndpoint = endpoint }, "ProjectSpace");
                }
            }
            catch (Exception exception) { logger.LogWarning(exception, "Could not refresh Project Space catalog."); }
            _projects = projects.OrderBy(project => project.Shortcut.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally { _gate.Release(); }
    }

    private static void AddProject(List<WebProject> projects, ProjectShortcut shortcut, string group)
    {
        if (string.IsNullOrEmpty(shortcut.ProjectFile) && string.IsNullOrEmpty(shortcut.ProjectSpaceGuid))
            return;
        if (string.IsNullOrEmpty(shortcut.ProjectFile) && string.IsNullOrEmpty(shortcut.ApiEndpoint))
            shortcut.ApiEndpoint = ProjectShortcut.CurrentApiEndpoint;
        var identity = !string.IsNullOrEmpty(shortcut.ProjectFile)
            ? Path.GetFullPath(shortcut.ProjectFile).ToUpperInvariant()
            : shortcut.ApiEndpoint.TrimEnd('/').ToUpperInvariant() + "/" + shortcut.ProjectSpaceGuid.ToUpperInvariant();
        var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..24];
        if (projects.All(project => project.Id != id))
            projects.Add(new WebProject(id, shortcut, group));
    }

    public WebProject GetProject(string id) => Projects.SingleOrDefault(project => project.Id == id)
        ?? throw new KeyNotFoundException("Project was not found in the dashboard catalog.");

    public async Task<ProjectForm> LoadFormAsync(string id)
    {
        var project = await GetProject(id).Shortcut.LoadAsync();
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        FlowBloxProjectManager.Instance.InitializeScopedProject(project);
        try
        {
            var fields = project.FlowBloxRegistry.GetUserFields(UserFieldTypes.Input);
            return new ProjectForm
            {
                Description = project.ProjectDescription ?? string.Empty,
                Notice = project.Notice ?? string.Empty,
                InputDirectory = project.ProjectInputDirectory,
                OutputDirectory = project.ProjectOutputDirectory,
                Fields = fields,
                FieldReferences = project.GetProjectPropertyElements()
                    .Select(property => new FieldReferenceOption { Name = property.Key, Group = "ProjectFields", Reference = "$Project::" + property.Key })
                    .Concat(AvailableOptions.Select(option => new FieldReferenceOption { Name = option.Name, Group = "Options", Reference = "$Options::" + option.Name }))
                    .Concat(FlowBloxVault.GetReferences().Select(entry => new FieldReferenceOption { Name = entry.Name, Group = "Vault", Reference = entry.Reference }))
                    .ToList(),
                Values = ProjectMetadataReader.Read(project).InputFields.ToDictionary(
                    field => field.Name, field => field.DefaultValue ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            };
        }
        finally { FlowBloxProjectManager.Instance.CloseScopedProject(project); }
    }

    public IReadOnlyList<OptionElement> AvailableOptions => FlowBloxOptions.GetOptionInstance().OptionCollection.Values
        .Where(option => !option.SystemOption && option.Type != OptionElement.OptionType.Password && option.IsPlaceholderEnabled)
        .OrderBy(option => option.Name).ToList();

    public async Task<ProjectExecutionEntry> StartAsync(string id, ProjectForm form)
    {
        await _gate.WaitAsync();
        try
        {
            var shortcut = GetProject(id).Shortcut;
            var project = await shortcut.LoadAsync();
            using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
            FlowBloxProjectManager.Instance.InitializeScopedProject(project);
            try
            {
                var fields = project.FlowBloxRegistry.GetUserFields(UserFieldTypes.Input);
                foreach (var field in fields)
                {
                    if (field.IsRequiredAtRuntimeStartup && (!form.Values.TryGetValue(field.Name, out var value) || string.IsNullOrWhiteSpace(value)))
                        throw new InvalidOperationException(field.Name);
                    if (field.GetValueDropDownMode() == FlowBlox.Core.Attributes.DropDownMode.SelectionOnly
                        && form.Values.TryGetValue(field.Name, out var selected) && !string.IsNullOrWhiteSpace(selected)
                        && field.GetPossibleValues().All(item => item.Value != selected))
                        throw new InvalidOperationException(field.Name);
                }
                var allowed = AvailableOptions.Select(option => option.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (form.Overrides.Keys.Any(key => !allowed.Contains(key)))
                    throw new InvalidOperationException("Option override is not available to the web dashboard.");
                var request = new RunnerRequest
                {
                    UserFields = fields.ToDictionary(field => field.Name, field => form.Values.GetValueOrDefault(field.Name) ?? string.Empty, StringComparer.OrdinalIgnoreCase),
                    OptionOverrides = new(form.Overrides, StringComparer.OrdinalIgnoreCase)
                };
                var input = ResolveDirectory(project, form.Overrides, OptionsConstants.PathsInputDir, project.ProjectInputDirectory);
                var output = ResolveDirectory(project, form.Overrides, OptionsConstants.PathsOutputDir, project.ProjectOutputDirectory);
                return Executions.Start(shortcut, project, request, input, output);
            }
            finally { FlowBloxProjectManager.Instance.CloseScopedProject(project); }
        }
        finally { _gate.Release(); }
    }

    private static string ResolveDirectory(FlowBloxProject project, Dictionary<string, string> overrides, string key, string fallback) =>
        overrides.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.Combine(Environment.ExpandEnvironmentVariables(value), IOUtil.GetValidFileName(project.ProjectName).Trim()) : fallback;

    public ProjectExecutionEntry GetExecution(Guid id) => Executions.GetAll().Single(entry => entry.Id == id);

    public async Task<ProjectArtifacts> ArtifactsAsync(string projectId, Guid? executionId = null)
    {
        var project = GetProject(projectId);
        if (executionId.HasValue)
        {
            var entry = GetExecution(executionId.Value);
            if (!ProjectShortcutIdentity.Matches(entry.Project, project.Shortcut))
                throw new UnauthorizedAccessException("Execution does not belong to this project.");
            return new ProjectArtifacts(entry.OutputDirectory);
        }
        var form = await LoadFormAsync(projectId);
        return new ProjectArtifacts(form.OutputDirectory);
    }

    public async Task ClearArtifactsAsync(string projectId, Guid? executionId)
    {
        await _gate.WaitAsync();
        try
        {
            var artifacts = await ArtifactsAsync(projectId, executionId);
            Executions.ClearArtifacts(artifacts.RootDirectory);
        }
        finally { _gate.Release(); }
    }

}
