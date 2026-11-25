namespace FlowBlox.AIAssistant.Tools.Util;

internal sealed class RetainedContentScopeLease(
    AsyncLocal<RetainedContentScope?> scope,
    RetainedContentScope? previous) : IDisposable
{
    public void Dispose() => scope.Value = previous;
}
