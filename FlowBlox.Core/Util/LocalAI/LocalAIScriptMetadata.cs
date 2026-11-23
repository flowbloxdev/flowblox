namespace FlowBlox.Core.Util.LocalAI;

/// <summary>
/// Describes a bundled model bootstrap and the matching runtime configuration.
/// Localized card text uses the model identifier as its resource key prefix.
/// </summary>
public sealed class LocalAIScriptMetadata
{
    public LocalAIModel Model { get; init; }
    public string ScriptName { get; init; }
    public string ModelFolder { get; init; }
    public string ChatTemplate { get; init; }
    public bool IsGenAI => ChatTemplate != null;
}
