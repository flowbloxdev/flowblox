namespace FlowBlox.Core.Models.Project;

public sealed class ProjectMetadata
{
    public string ProjectName { get; init; }
    public string ShortDescription { get; init; }
    public string Description { get; init; }
    public IReadOnlyList<ProjectFieldMetadata> InputFields { get; init; } = [];
    public IReadOnlyList<ProjectFieldMetadata> MemoryFields { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Outputs { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}
