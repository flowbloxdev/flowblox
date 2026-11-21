using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Provider;

namespace FlowBlox.Core.Util.Projects;

/// <summary>
/// Transfers editable Project Space projects without starting a runtime or materializing input files.
/// </summary>
public sealed class ProjectSpaceTools
{
    private readonly FlowBloxWebApiService _api;
    private readonly string _token;

    public ProjectSpaceTools(FlowBloxWebApiService api, string token = "")
    {
        _api = api;
        _token = token;
    }

    public async Task<List<FbProjectResult>> ListAsync(bool privateProjects, string query, bool mine = false, FbProjectClassification? classification = null)
    {
        var response = await _api.GetProjectsAsync(_token, privateProjects || mine, query, classification);
        RequireSuccess(response);
        return (response.ResultObject ?? new())
            .Where(project => mine || project.Visibility == (privateProjects ? FbProjectVisibility.Private : FbProjectVisibility.Public))
            .ToList();
    }

    public async Task<ProjectSpaceTransferResult> DownloadAsync(string guid, string directory, bool overwrite)
    {
        var metadata = await GetMetadataAsync(guid);
        var fileName = IOUtil.GetValidFileName(metadata.Name).Trim();
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = guid;
        var file = Path.GetFullPath(Path.Combine(directory, fileName + ".fbprj"));
        return await DownloadToFileAsync(metadata, file, overwrite);
    }

    public async Task<ProjectSpaceTransferResult> ReloadAsync(string file, bool overwrite)
    {
        file = Path.GetFullPath(file);
        var project = FlowBloxProject.FromFile(file);
        if (string.IsNullOrWhiteSpace(project.ProjectSpaceGuid))
            throw new InvalidOperationException("Reload requires a local project bound to Project Space.");
        if (!string.Equals(project.ProjectSpaceEndpointUri?.TrimEnd('/'), _api.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The project is bound to a different Project Space endpoint.");
        var metadata = await GetMetadataAsync(project.ProjectSpaceGuid);
        return await DownloadToFileAsync(metadata, file, overwrite);
    }

    private async Task<ProjectSpaceTransferResult> DownloadToFileAsync(FbProjectResult metadata, string file, bool overwrite)
    {
        foreach (var candidate in new[] { file, Path.ChangeExtension(file, ".fbdeps"), Path.ChangeExtension(file, ".fblocaldata") })
        {
            if (System.IO.File.Exists(candidate) && !overwrite)
                throw new IOException($"File already exists: {candidate}. Use --overwrite to replace the local copy.");
        }

        var project = await FlowBloxProject.FromProjectSpaceGuidAsync(metadata.Guid, _token, _api);
        RegisterForSerialization(project);
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        project.Save(file);
        return Result(metadata, file);
    }

    public async Task<ProjectSpaceTransferResult> UploadAsync(
        string file,
        bool createNew,
        string? description,
        FbProjectVisibility? visibility,
        FbProjectClassification? classification = null)
    {
        file = Path.GetFullPath(file);
        var project = FlowBloxProject.FromFile(file);
        var guid = project.ProjectSpaceGuid;
        if (!createNew && !string.IsNullOrWhiteSpace(guid)
            && !string.Equals(project.ProjectSpaceEndpointUri?.TrimEnd('/'), _api.BaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The project is bound to a different Project Space endpoint. Use --new to create a separate project.");

        var existing = !createNew && !string.IsNullOrWhiteSpace(guid) ? await GetMetadataAsync(guid) : null;
        var effectiveDescription = project.UseProjectDescriptionForProjectSpace
            ? project.ProjectDescription
            : description ?? existing?.Description;
        if (string.IsNullOrWhiteSpace(project.ProjectName) || string.IsNullOrWhiteSpace(effectiveDescription))
            throw new InvalidOperationException("Project name and description are required. Set ProjectDescription or provide --description when UseProjectDescriptionForProjectSpace is false.");
        if (description != null && project.UseProjectDescriptionForProjectSpace)
            throw new InvalidOperationException("--description requires UseProjectDescriptionForProjectSpace=false. Otherwise the project's description is used.");

        RegisterForSerialization(project);
        using var scope = FlowBloxRegistryProvider.BeginScopedRegistry(project.FlowBloxRegistry);
        if (createNew && !string.IsNullOrWhiteSpace(guid))
            project.ProjectGuid = Guid.NewGuid();
        var content = project.CreateProjectSpaceZipBase64();
        var effectiveVisibility = visibility ?? existing?.Visibility ?? FbProjectVisibility.Private;
        if (existing == null)
        {
            var response = await _api.CreateProjectAsync(_token, new FbProjectCreateRequest
            {
                Classification = classification ?? FbProjectClassification.Solution,
                Name = project.ProjectName,
                Description = effectiveDescription,
                Visibility = effectiveVisibility,
                ContentBase64 = content
            });
            RequireSuccess(response);
            if (!Guid.TryParse(response.ProjectGuid, out _))
                throw new InvalidDataException("Project Space did not return a valid project GUID after upload.");
            guid = response.ProjectGuid;
        }
        else
        {
            var response = await _api.UpdateProjectAsync(_token, new FbProjectChangeRequest
            {
                Classification = classification,
                ProjectGuid = guid,
                Name = project.ProjectName,
                Description = effectiveDescription,
                Visibility = effectiveVisibility,
                ContentBase64 = content
            });
            RequireSuccess(response);
        }

        project.ProjectSpaceGuid = guid;
        project.ProjectSpaceEndpointUri = _api.BaseUrl;
        project.ProjectSpaceVersion = null;
        project.Save(file);
        return new ProjectSpaceTransferResult
        {
            ProjectSpaceGuid = guid,
            Name = project.ProjectName,
            Description = effectiveDescription,
            Visibility = effectiveVisibility,
            File = file
        };
    }

    private async Task<FbProjectResult> GetMetadataAsync(string guid)
    {
        if (!Guid.TryParse(guid, out _))
            throw new ArgumentException("A valid Project Space GUID is required.");
        var response = await _api.GetProjectAsync(new FbProjectRequest { Guid = guid }, _token);
        RequireSuccess(response);
        return response.ResultObject ?? throw new InvalidDataException("Project Space returned no project metadata.");
    }

    private static void RegisterForSerialization(FlowBloxProject project)
    {
        // Register the imported graph only; OnProjectLoaded also writes input files and invokes component hooks.
        foreach (var block in project.FlowBlocks ?? new())
            project.FlowBloxRegistry.RegisterFlowBlock(block);
        foreach (var managedObject in project.ManagedObjects ?? new())
            project.FlowBloxRegistry.Register(managedObject);
        foreach (var field in project.UserFields ?? new())
            project.FlowBloxRegistry.Register(field);
    }

    private static ProjectSpaceTransferResult Result(FbProjectResult metadata, string file) => new()
    {
        ProjectSpaceGuid = metadata.Guid,
        Name = metadata.Name,
        Description = metadata.Description,
        Visibility = metadata.Visibility,
        File = file
    };

    private static void RequireSuccess(FlowBloxWebApiService.ApiResponse? response)
    {
        if (response?.Success != true)
            throw new InvalidOperationException(response?.ErrorMessage ?? "Project Space request failed.");
    }
}
