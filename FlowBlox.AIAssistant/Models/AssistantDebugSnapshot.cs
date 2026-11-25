using FlowBlox.AIAssistant.Builder;

namespace FlowBlox.AIAssistant.Models;

public enum AssistantSystemMessageKind
{
    SystemPrompt,
    SessionBootstrap,
    Explanation,
    TypeInfo
}

public sealed record AssistantSystemMessage(
    string Key,
    string Title,
    AssistantSystemMessageKind Kind,
    string Content,
    bool IsRuntimeBound = false);

public sealed record AssistantDebugHistoryMessage(AssistantSessionMessage Message, bool IsSummarized);

public sealed record AssistantDebugSnapshot(
    string Summary,
    IReadOnlyList<AssistantDebugHistoryMessage> History,
    IReadOnlyList<AssistantSystemMessage> SystemMessages,
    double CharactersPerToken);
