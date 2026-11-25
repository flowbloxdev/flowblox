namespace FlowBlox.AIAssistant.Tools.State;

internal static class AssistantSessionContentStore
{
    public static DateTimeOffset ApplicationStartedAt { get; } = ReadApplicationStartedAt();

    private static DateTimeOffset ReadApplicationStartedAt()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return process.StartTime.ToUniversalTime();
    }
}
