namespace FlowBlox.Core.Models.Project.Launcher;

public sealed class ProjectArtifactEntry
{
    public string Name { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
    public DateTime ModifiedUtc { get; set; }
}
