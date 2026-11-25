using System.Globalization;
using FlowBlox.AIAssistant.Models;
using MahApps.Metro.IconPacks;

namespace FlowBlox.UICore.ViewModels;

public sealed record AiAssistantDebugCard(
    string Key,
    string Title,
    string Preview,
    string ResponsePreview,
    string Content,
    string Type,
    long EstimatedTokens,
    bool IsPinned,
    PackIconMaterialKind Icon,
    string Status)
{
    public AssistantSystemMessageKind? SystemMessageKind { get; init; }
    public IReadOnlyList<AiAssistantUsageMetric> UsageMetrics { get; init; } = [];
    public bool HasUsageMetrics => UsageMetrics.Count > 0;
    public bool HasResponse => !string.IsNullOrEmpty(ResponsePreview);
    public string Origin => AiAssistantDebugViewModel.Text(IsPinned ? "Dynamic" : "Static");
    public string TokensText => string.Format(
        CultureInfo.CurrentCulture,
        AiAssistantDebugViewModel.Text(Key == "usage" ? "ProcessedTokens" : "Tokens"),
        EstimatedTokens);
    public string PinTooltip => AiAssistantDebugViewModel.Text("RuntimeBindingTooltip");
    public string OriginTooltip => AiAssistantDebugViewModel.Text(IsPinned ? "RuntimeBindingTooltip" : "StaticContentTooltip");
}
