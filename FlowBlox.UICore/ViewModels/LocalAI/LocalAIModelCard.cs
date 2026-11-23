using FlowBlox.Core.Util.LocalAI;

namespace FlowBlox.UICore.ViewModels.LocalAI;

public sealed class LocalAIModelCard
{
    public LocalAIModel Model { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Details { get; init; } = string.Empty;
}
