using System.Collections.Concurrent;
using FlowBlox.AIAssistant.Models;
using Newtonsoft.Json.Linq;

namespace FlowBlox.AIAssistant.Tools.State;

// Runtime-only explanation bindings. Type schemas remain in tool responses.
internal static class AssistantSessionContentStore
{
    private static readonly ConcurrentDictionary<string, AssistantSessionContentCache> Sessions = new(StringComparer.Ordinal);

    public static AssistantSessionContentCache Get(string sessionId) => Sessions.GetOrAdd(sessionId, _ => new());

    public static void Clear(string sessionId) => Sessions.TryRemove(sessionId, out _);

    public static DateTimeOffset ApplicationStartedAt { get; } = ReadApplicationStartedAt();

    private static DateTimeOffset ReadApplicationStartedAt()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }
}

internal sealed class AssistantSessionContentCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AssistantSystemMessage> _bindings = new(StringComparer.Ordinal);

    public IReadOnlyList<AssistantSystemMessage> GetSystemMessages()
    {
        lock (_sync)
            return _bindings.Values.ToArray();
    }

    public ToolResponse LoadExplanation(
        string key,
        string title,
        string content)
    {
        lock (_sync)
        {
            var bindingKey = "explanation:" + key;
            if (_bindings.ContainsKey(bindingKey))
                return new()
                {
                    Ok = true,
                    Result = new JObject
                    {
                        ["key"] = key,
                        ["loaded"] = false,
                        ["message"] = $"Already loaded: '{title}' is available as a system message in this session."
                    }
                };

            _bindings.Add(bindingKey, new(
                bindingKey,
                title,
                AssistantSystemMessageKind.Explanation,
                content,
                true));
            return new()
            {
                Ok = true,
                Result = new JObject
                {
                    ["key"] = key,
                    ["loaded"] = true,
                    ["message"] = $"'{title}' was loaded and will be available as a system message from the next request."
                }
            };
        }
    }

}
