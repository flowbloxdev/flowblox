using FlowBlox.Core.Models.FlowBlocks.AIRemote.Base;

namespace FlowBloxTest.Services;

internal static class AiChatRequestTestExtensions
{
    public static string GetInputText(this AIChatRequest request) =>
        string.Join("\n", request.Messages.Select(message => message.Content));
}
