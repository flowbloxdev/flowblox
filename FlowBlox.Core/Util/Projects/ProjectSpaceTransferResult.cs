using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;

namespace FlowBlox.Core.Util.Projects;

public sealed class ProjectSpaceTransferResult
{
    public string ProjectSpaceGuid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FbProjectVisibility Visibility { get; set; }
    public string File { get; set; } = string.Empty;
}
