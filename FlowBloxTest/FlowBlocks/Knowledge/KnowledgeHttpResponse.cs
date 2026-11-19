namespace FlowBloxTest.FlowBlocks.Knowledge;

public sealed class KnowledgeHttpResponse
{
    public int Status { get; init; } = 200;
    public string ContentType { get; init; } = "application/json";
    public string Body { get; init; } = "{}";
}
