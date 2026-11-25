using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Tools.Util;

internal sealed class RetainedContentScope(
    Func<string, string?> findLocation,
    bool requireToolDefinitions)
{
    private readonly List<AssistantRetainedContent> _pending = new();

    public bool IsToolDefinitionMissing(string key) => requireToolDefinitions && findLocation(key) == null;

    public string? FindLocation(string key)
    {
        return findLocation(key) ?? (_pending.Any(content => content.Key == key) ? "history" : null);
    }

    public void Track(AssistantRetainedContent content)
    {
        if (!string.IsNullOrWhiteSpace(content.Content))
            _pending.Add(content);
    }
}
