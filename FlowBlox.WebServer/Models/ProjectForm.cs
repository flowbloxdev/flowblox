using FlowBlox.Core.Models.Components;

namespace FlowBlox.WebServer.Models;

public sealed class ProjectForm
{
    public string Description { get; init; } = string.Empty;
    public string Notice { get; init; } = string.Empty;
    public string InputDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public List<FieldElement> Fields { get; init; } = [];
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Overrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
