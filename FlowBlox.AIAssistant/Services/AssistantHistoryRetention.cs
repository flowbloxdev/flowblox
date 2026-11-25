using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Services;

/// <summary>
/// Verbatim Context Retention protects reusable tool content using shared retrieval-based promotion.
/// </summary>
internal static class AssistantHistoryRetention
{
    public static IReadOnlyList<AssistantSystemMessage> GetSystemMessages(IReadOnlyList<AssistantRetainedContent> retainedContents)
    {
        return retainedContents.Select(content => new AssistantSystemMessage(
            content.Key,
            string.IsNullOrWhiteSpace(content.Title) ? content.Key : content.Title,
            content.Kind,
            content.Content,
            IsRuntimeBound: true)).ToArray();
    }

    public static string? FindLocation(
        IReadOnlyList<AssistantSessionMessage> messages,
        IReadOnlyList<AssistantRetainedContent> retainedContents,
        string key)
    {
        if (retainedContents.Any(content => content.Key == key))
            return "system";

        return messages.SelectMany(message => message.RetainedContents)
            .Any(content => content.Key == key && !string.IsNullOrWhiteSpace(content.Content))
            ? "history"
            : null;
    }

    /// <summary>
    /// Copies eligible content at a committed context transition, preserving original history and indexes.
    /// </summary>
    public static void Promote(
        IEnumerable<AssistantSessionMessage> messages,
        IList<AssistantRetainedContent> retainedContents)
    {
        foreach (var content in GetPendingPromotions(messages, retainedContents))
            retainedContents.Add(content);
    }

    /// <summary>
    /// Previews the same promotions used at the context transition without modifying history or system messages.
    /// </summary>
    public static IReadOnlyList<AssistantRetainedContent> GetPendingPromotions(
        IEnumerable<AssistantSessionMessage> messages,
        IEnumerable<AssistantRetainedContent> retainedContents)
    {
        var known = retainedContents.Select(content => content.Key).ToHashSet(StringComparer.Ordinal);
        var marked = messages.SelectMany(message => message.RetainedContents)
            .Where(content => !string.IsNullOrWhiteSpace(content.Key)).ToArray();
        var payloads = marked.Where(content => !string.IsNullOrWhiteSpace(content.Key) &&
                !string.IsNullOrWhiteSpace(content.Content))
            .GroupBy(content => content.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var retrievals = new Dictionary<string, int>(StringComparer.Ordinal);
        var promotions = new List<AssistantRetainedContent>();
        foreach (var content in marked)
        {
            if (known.Contains(content.Key))
                continue;

            retrievals.TryGetValue(content.Key, out var count);
            retrievals[content.Key] = ++count;
            var requiredRetrievals = content.Kind == AssistantSystemMessageKind.Explanation ? 1 : 2;
            if (count < requiredRetrievals)
                continue;
            if (!payloads.TryGetValue(content.Key, out var payload))
                continue;

            // Append at the promotion event, never regroup content by kind.
            promotions.Add(payload.Clone());
            known.Add(content.Key);
        }
        return promotions;
    }
}
