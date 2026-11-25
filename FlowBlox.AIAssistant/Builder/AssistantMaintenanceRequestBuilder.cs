using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBlox.AIAssistant.Builder;

/// <summary>
/// Appends maintenance instructions without changing the existing prefix or provider metadata.
/// </summary>
internal static class AssistantMaintenanceRequestBuilder
{
    public const string ModeMarker = "FLOWBLOX_CONTEXT_MAINTENANCE";

    public static AIChatRequest Append(AIChatRequest prefix, AIChatRequest operation)
    {
        var request = new AIChatRequest { Source = operation.Source };
        request.SystemMessages.AddRange(prefix.SystemMessages);
        request.Messages.AddRange(prefix.Messages);
        foreach (var entry in operation.Meta)
            request.Meta[entry.Key] = entry.Value;
        request.Messages.Add(new AIChatMessage
        {
            Role = "user",
            Content = ModeMarker + "\n\n" +
                string.Join("\n\n", operation.SystemMessages.Concat(operation.Messages).Select(message => message.Content))
        });
        return request;
    }

    public static AIChatRequest Continue(AIChatRequest request, string response)
    {
        var continuation = new AIChatRequest { Source = request.Source };
        continuation.SystemMessages.AddRange(request.SystemMessages);
        continuation.Messages.AddRange(request.Messages);
        continuation.Messages.Add(new AIChatMessage { Role = "assistant", Content = response });
        return continuation;
    }
}
