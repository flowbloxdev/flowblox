using FlowBlox.AIAssistant.Models;

namespace FlowBlox.AIAssistant.Tools.Util
{
    internal static partial class ToolHandlerUtilities
    {
        private sealed class ComponentScopeLease : IDisposable
        {
            private readonly AssistantComponentContext? _previous;

            public ComponentScopeLease(AssistantComponentContext? previous) => _previous = previous;

            public void Dispose() => ComponentScope.Value = _previous;
        }
    }
}
