using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi;
using FlowBlox.Core.Util;
using Newtonsoft.Json.Linq;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectShortcut
{
    public string Name { get; set; } = string.Empty;
    public string ProjectFile { get; set; } = string.Empty;
    public string ProjectSpaceGuid { get; set; } = string.Empty;
    public string ApiEndpoint { get; set; } = string.Empty;

    public static string CurrentApiEndpoint => FlowBloxOptions.GetOptionInstance()
        .GetOption(OptionsConstants.ApiProjectServiceBaseUrl)?.Value ?? string.Empty;

    public static ProjectShortcut FromFile(string path)
    {
        var json = JObject.Parse(File.ReadAllText(path));
        var localDataFile = Path.ChangeExtension(path, ".fblocaldata");
        var localData = File.Exists(localDataFile)
            ? JObject.Parse(File.ReadAllText(localDataFile))
            : new JObject();
        return new ProjectShortcut
        {
            Name = json.Value<string>("ProjectName") ?? Path.GetFileNameWithoutExtension(path),
            ProjectFile = Path.GetFullPath(path),
            ProjectSpaceGuid = localData.Value<string>("ProjectSpaceGuid") ?? string.Empty,
            ApiEndpoint = localData.Value<string>("ProjectSpaceEndpointUri") ?? string.Empty
        };
    }

    public async Task<FlowBloxProject> LoadAsync()
    {
        if (!string.IsNullOrWhiteSpace(ProjectFile))
            return await Task.Run(() => FlowBloxProject.FromFile(ProjectFile));

        var endpoint = string.IsNullOrWhiteSpace(ApiEndpoint) ? CurrentApiEndpoint : ApiEndpoint;
        var token = FlowBloxAccountManager.Instance.GetUserToken(endpoint);
        return await FlowBloxProject.FromProjectSpaceGuidAsync(
            ProjectSpaceGuid, token, new FlowBloxWebApiService(endpoint));
    }
}
