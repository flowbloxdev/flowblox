namespace FlowBlox.AIAssistant.Models;

/// <summary>
/// Tool content protected from compaction and eligible for verbatim retention as a system message.
/// </summary>
public sealed class AssistantRetainedContent
{
    public string Key { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public AssistantSystemMessageKind Kind { get; set; } = AssistantSystemMessageKind.TypeInfo;
    public int ResultIndex { get; set; } = -1;
    public int? BatchResultIndex { get; set; }
    public string? PayloadPath { get; set; }

    public AssistantRetainedContent Clone() => new()
    {
        Key = Key,
        Content = Content,
        Title = Title,
        Kind = Kind,
        ResultIndex = ResultIndex,
        BatchResultIndex = BatchResultIndex,
        PayloadPath = PayloadPath
    };
}
