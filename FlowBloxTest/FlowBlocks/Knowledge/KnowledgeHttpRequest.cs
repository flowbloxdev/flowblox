using Newtonsoft.Json.Linq;

namespace FlowBloxTest.FlowBlocks.Knowledge;

public sealed class KnowledgeHttpRequest
{
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string Body { get; init; } = string.Empty;
    public JObject Json => string.IsNullOrEmpty(Body) ? new JObject() : JObject.Parse(Body);
}
