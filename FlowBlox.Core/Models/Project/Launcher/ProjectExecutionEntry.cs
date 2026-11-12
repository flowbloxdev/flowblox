using FlowBlox.Core.Runner.Contracts;
using Newtonsoft.Json;

namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectExecutionEntry
{
    public Guid Id { get; set; }
    public ProjectShortcut Project { get; set; } = new();
    public RunnerRequest Request { get; set; } = new();
    public string RequestFile { get; set; } = string.Empty;
    public string ResponseFile { get; set; } = string.Empty;
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public RunnerResponse? Response { get; set; }
    public string LaunchError { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsRunning => Response == null && string.IsNullOrEmpty(LaunchError);
}
