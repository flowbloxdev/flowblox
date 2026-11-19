namespace FlowBlox.Core.Util.Projects;

internal sealed class ProjectSpaceCacheEntry
{
    public string Fingerprint { get; set; }
    public string ContentHash { get; set; }
    public string Content { get; set; }
}
