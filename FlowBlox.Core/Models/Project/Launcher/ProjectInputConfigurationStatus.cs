namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectInputConfigurationStatus
{
    public int TotalCount { get; init; }
    public int FilledCount { get; init; }
    public IReadOnlyList<string> MissingRequiredFields { get; init; } = [];
}
