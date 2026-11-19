using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Models.Project;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.Util;

internal sealed class ProjectSpaceToolClient
{
    private const string LoginHint = "Private Project Space access requires a current login. Sign in to the configured Project Space endpoint with Remember credentials enabled, then retry with private=true (or visibility=private/all for GetPSProjects).";

    public FlowBloxWebApiService Api { get; }
    public string Token { get; }

    private ProjectSpaceToolClient(string endpoint, string token)
    {
        Api = new FlowBloxWebApiService(endpoint);
        Token = token;
    }

    public static async Task<ProjectSpaceToolClient> CreateAsync(bool requireLogin)
    {
        var endpoint = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl).Value;
        var manager = FlowBloxAccountManager.Instance;
        if (requireLogin && !manager.IsLoggedInFor(endpoint))
            await new FlowBloxAutoLoginExecutor().TryAutoLoginAsync().ConfigureAwait(false);
        var token = requireLogin ? manager.GetUserToken(endpoint) : string.Empty;
        if (requireLogin && (!manager.IsLoggedInFor(endpoint) || string.IsNullOrWhiteSpace(token)))
            throw new InvalidOperationException(LoginHint);
        return new ProjectSpaceToolClient(endpoint, token);
    }

    public static IEnumerable<int> StableVersions(FbProjectResult project) =>
        (project.Versions ?? [])
            .Where(version => version.VersionNumber > 0 && version.HasContent != false)
            .Select(version => version.VersionNumber)
            .Distinct()
            .OrderDescending();

    public static async Task<FlowBloxProject> LoadAsync(JObject args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Guid.TryParse(args.Value<string>("projectSpaceGuid"), out var guid))
            throw new InvalidOperationException("A valid projectSpaceGuid is required.");
        if (!string.IsNullOrWhiteSpace(args.Value<string>("projectFile")))
            throw new InvalidOperationException("Provide either projectFile or projectSpaceGuid, not both.");
        var version = args.Value<int?>("projectSpaceVersion");
        if (version <= 0)
            throw new InvalidOperationException("projectSpaceVersion must be a positive stable version number.");
        var client = await CreateAsync(args.Value<bool?>("private") ?? false).ConfigureAwait(false);
        var response = await client.Api.GetProjectAsync(new FbProjectRequest { Guid = guid.ToString("N") }, client.Token).ConfigureAwait(false);
        RequireSuccess(response);
        var metadata = response.ResultObject ?? throw new InvalidOperationException("Project Space returned no project metadata.");
        version ??= StableVersions(metadata).Cast<int?>().FirstOrDefault();
        if (version.HasValue && !StableVersions(metadata).Contains(version.Value))
            throw new InvalidOperationException($"Stable project version {version} was not found.");
        using var extensionLoading = new DisableExtensionLoading();
        var project = await FlowBloxProject.FromProjectSpaceGuidAsync(
            guid.ToString("N"), version, client.Token, client.Api).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return project;
    }

    public static void RequireSuccess(FlowBloxWebApiService.ApiResponse response)
    {
        if (response.Success)
            return;
        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            throw new InvalidOperationException(LoginHint);
        throw new InvalidOperationException(response.ErrorMessage ?? "Project Space request failed.");
    }
}
