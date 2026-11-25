using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Tools.Util;

internal static partial class ToolHandlerUtilities
{
    private static readonly AsyncLocal<RetainedContentScope?> RetainedContentScope = new();

    public static IDisposable BeginRetainedContentScope(
        Func<string, string?> findLocation,
        bool requireToolDefinitions = false)
    {
        var previous = RetainedContentScope.Value;
        RetainedContentScope.Value = new RetainedContentScope(findLocation, requireToolDefinitions);
        return new RetainedContentScopeLease(RetainedContentScope, previous);
    }

    public static string? GetRetainedContentLocation(string key) => RetainedContentScope.Value?.FindLocation(key);

    public static bool IsToolDefinitionMissing(string key) =>
        RetainedContentScope.Value?.IsToolDefinitionMissing(key) == true;

    public static void RegisterRetainedContent(ToolResponse response, AssistantRetainedContent content)
    {
        response.RetainedContents.Add(content);
        RetainedContentScope.Value?.Track(content);
    }
}
