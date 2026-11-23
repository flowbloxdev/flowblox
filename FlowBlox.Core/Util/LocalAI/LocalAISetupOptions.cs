namespace FlowBlox.Core.Util.LocalAI;

public sealed class LocalAISetupOptions
{
    public bool OverwriteProviderInput { get; init; }
    public bool OverwriteScript { get; init; }
    public bool OverwriteBlockProvider { get; init; } = true;
    public bool OverwriteBlockModel { get; init; } = true;
}
