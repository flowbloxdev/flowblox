using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Threading;
using FlowBlox.AIAssistant.Builder;
using FlowBlox.AIAssistant.Models;
using FlowBlox.AIAssistant.Services;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using MahApps.Metro.IconPacks;

namespace FlowBlox.UICore.ViewModels;

public enum AiAssistantDebugTopic
{
    Usage,
    Summary,
    History,
    SystemMessages
}
public sealed record AiAssistantDebugTopicCard(
    AiAssistantDebugTopic Topic,
    string Title,
    string Description);
public sealed record AiAssistantDebugCard(
    string Key,
    string Title,
    string Preview,
    string ResponsePreview,
    string Content,
    string Type,
    int EstimatedTokens,
    bool IsPinned,
    PackIconMaterialKind Icon,
    string Status)
{
    public bool HasResponse => !string.IsNullOrEmpty(ResponsePreview);
    public string TokensText => string.Format(
            CultureInfo.CurrentCulture,
            AiAssistantDebugViewModel.Text(Key == "usage" ? "ProcessedTokens" : "Tokens"),
            EstimatedTokens);
    public string PinTooltip => AiAssistantDebugViewModel.Text("RuntimeBindingTooltip");
}

public sealed class AiAssistantDebugViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AiAssistantService _service;
    private readonly Dispatcher _dispatcher;
    private AssistantDebugSnapshot _snapshot;
    private AiAssistantDebugTopicCard _selectedTopic;
    private AiAssistantDebugCard? _selectedCard;
    private bool _disposed;
    private int _refreshPending;

    public ObservableCollection<AiAssistantDebugTopicCard> Topics { get; } = new();
    public ObservableCollection<AiAssistantDebugCard> Cards { get; } = new();
    public AiAssistantChatViewModel Runtime { get; }
    public string RuntimeLabel => Text("Runtime_Label");
    public InnerContentViewModel Editor { get; } = new() { IsReadOnly = true };
    public string Title => Text("Title");
    public string PinTooltip => Text("RuntimeBindingTooltip");
    public string EmptyText => Text("Empty");
    public bool IsEmpty => Cards.Count == 0;
    public string Statistics { get; private set; } = string.Empty;
    public string ContextStatistics { get; private set; } = string.Empty;
    public AiAssistantDebugTopicCard SelectedTopic
    {
        get => _selectedTopic;
        set
        {
            if (value == null || _selectedTopic == value)
                return;

            _selectedTopic = value;
            Changed(nameof(SelectedTopic));
            RefreshCards();
        }
    }
    public AiAssistantDebugCard? SelectedCard
    {
        get => _selectedCard;
        set
        {
            if (_selectedCard == value)
                return;

            _selectedCard = value;
            Editor.ContentText = value?.Content ?? string.Empty;
            Changed(nameof(SelectedCard));
        }
    }

    public AiAssistantDebugViewModel(
        AiAssistantService service,
        AiAssistantChatViewModel runtime)
    {
        _service = service;
        Runtime = runtime;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Topics.Add(new(AiAssistantDebugTopic.Usage, Text("Usage"), Text("UsageDescription")));
        Topics.Add(new(
            AiAssistantDebugTopic.Summary,
            Text("Summary"),
            Text("SummaryDescription")));
        Topics.Add(new(
            AiAssistantDebugTopic.History,
            Text("History"),
            Text("HistoryDescription")));
        Topics.Add(new(
            AiAssistantDebugTopic.SystemMessages,
            Text("SystemMessages"),
            Text("SystemMessagesDescription")));
        _selectedTopic = Topics[0];
        _service.DebugStateChanged += ServiceChanged;
        _snapshot = _service.GetDebugSnapshot();
        RefreshCards();
    }

    private void ServiceChanged(
        object? sender,
        EventArgs args)
    {
        if (_disposed || Interlocked.Exchange(ref _refreshPending, 1) != 0)
            return;

        _dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref _refreshPending, 0);
            if (_disposed)
                return;

            _snapshot = _service.GetDebugSnapshot();
            RefreshCards();
        });
    }

    private int Estimate(string content) => string.IsNullOrEmpty(content) ? 0 :
            (int)Math.Ceiling(content.Length / Math.Max(1d, _snapshot.CharactersPerToken));

    private static string Preview(string content) => TextHelper.ShortenString(
            content,
            220,
            true);

    private void RefreshCards()
    {
        var cards = new List<AiAssistantDebugCard>();
        var rawSystemTokens = _snapshot.SystemMessages.Sum(x => Estimate(x.Content));
        var summaryTokens = Estimate(_snapshot.Summary);
        var activeTokens = _snapshot.History.Where(x => !x.IsSummarized)
            .Sum(x => Estimate(x.Message.CompleteMessage));
        var historicalTokens = _snapshot.History.Where(x => x.IsSummarized)
            .Sum(x => Estimate(x.Message.CompleteMessage));
        ContextStatistics = string.Format(
            CultureInfo.CurrentCulture,
            Text("ContextStatistics"),
            _service.CurrentContextTokens > 0 ? _service.CurrentContextTokens : rawSystemTokens + summaryTokens + activeTokens,
            _service.EffectiveContextLimit);
        switch (SelectedTopic.Topic)
        {
            case AiAssistantDebugTopic.Usage:
                var usage = _service.SessionUsage;
                var unavailable = Text("Unavailable");
                var detail = string.Format(
                    CultureInfo.CurrentCulture,
                    Text("UsageDetails"),
                    usage.UnpricedRequests == 0 ? usage.EstimatedCost.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : unavailable,
                    usage.UnpricedRequests == 0 ? usage.CostWithoutCaching.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : unavailable,
                    usage.UnpricedRequests == 0 ? usage.SavedByCaching.ToString("C2", CultureInfo.GetCultureInfo("en-US")) : unavailable,
                    usage.UnpricedRequests == 0 ? usage.CostSavingRate?.ToString("P1") ?? unavailable : unavailable,
                    _service.CurrentContextTokens,
                    _service.EffectiveContextLimit,
                    _service.EffectiveContextLimit > 0 ? (double)_service.CurrentContextTokens / _service.EffectiveContextLimit : 0,
                    usage.TotalInputTokens,
                    usage.CachedInputTokens,
                    usage.UncachedInputTokens,
                    usage.CacheWriteInputTokens,
                    usage.OutputTokens,
                    usage.TotalTokens,
                    usage.IncompleteUsageRequests == 0 ? usage.CacheHitRate?.ToString("P1") ?? unavailable : unavailable,
                    usage.UnpricedRequests,
                    usage.IncompleteUsageRequests);
                Statistics = Text("Usage");
                cards.Add(new("usage", Text("Usage"), Preview(detail), "", detail, Text("Usage"), (int)Math.Min(int.MaxValue, usage.TotalTokens), false,
                    PackIconMaterialKind.InformationOutline, ""));
                break;
            case AiAssistantDebugTopic.Summary:
                Statistics = string.Format(
                    CultureInfo.CurrentCulture,
                    Text("Tokens"),
                    summaryTokens);
                if (!string.IsNullOrWhiteSpace(_snapshot.Summary))
                    cards.Add(new(
                        "summary",
                        Text("Summary"),
                        Preview(_snapshot.Summary),
                        "",
                        _snapshot.Summary,
                        Text("Summary"),
                        summaryTokens,
                        false,
                        PackIconMaterialKind.TextBoxOutline,
                        Text("Summary")));
                break;
            case AiAssistantDebugTopic.History:
                Statistics = string.Format(
                    CultureInfo.CurrentCulture,
                    Text("HistoryStatistics"),
                    activeTokens,
                    historicalTokens);
                for (var i = 0; i < _snapshot.History.Count; i++)
                {
                    var entry = _snapshot.History[i];
                    var pair = entry.Message as AssistantMessagePair;
                    var firstRole = pair != null || entry.Message.Role == "assistant" ? Text("AI") : Text("User");
                    var first = pair?.AssistantRequest ?? entry.Message.CompleteMessage;
                    var response = pair?.ToolApiResponse ?? "";
                    var status = Text(entry.IsSummarized ? "Summarized" : "Active");
                    var content = firstRole + ":\n" + first + (pair != null ? "\n\n" + Text("ToolAPI") + ":\n" + response : "");
                    cards.Add(new(
                        "history:" + i,
                        $"{i + 1} · {firstRole}",
                        Preview(first),
                        pair != null ? Text("ToolAPI") + ": " + Preview(response) : "",
                        content,
                        Text(pair != null ? "Pair" : "Single"),
                        Estimate(entry.Message.CompleteMessage),
                        false,
                        entry.IsSummarized ? PackIconMaterialKind.ArchiveOutline : PackIconMaterialKind.MessageTextOutline,
                        status));
                }
                break;
            case AiAssistantDebugTopic.SystemMessages:
                Statistics = string.Format(
                    CultureInfo.CurrentCulture,
                    Text("SystemStatistics"),
                    rawSystemTokens,
                    rawSystemTokens);
                foreach (var message in _snapshot.SystemMessages)
                    cards.Add(new(
                        message.Key,
                        message.IsRuntimeBound ? message.Title : Text(message.Kind.ToString()),
                        Preview(message.Content),
                        "",
                        message.Content,
                        Text(message.Kind.ToString()),
                        Estimate(message.Content),
                        message.IsRuntimeBound,
                        PackIconMaterialKind.TextBoxOutline,
                        Text(message.Kind.ToString())));
                break;
        }
        var selectedKey = SelectedCard?.Key;
        for (var i = 0; i < cards.Count; i++)
        {
            if (i == Cards.Count)
                Cards.Add(cards[i]);
            else if (Cards[i] != cards[i])
                    Cards[i] = cards[i];
        }

        while (Cards.Count > cards.Count)
            Cards.RemoveAt(Cards.Count - 1);

        SelectedCard = Cards.FirstOrDefault(x => x.Key == selectedKey) ?? Cards.FirstOrDefault();
        Changed(nameof(Statistics));
        Changed(nameof(ContextStatistics));
        Changed(nameof(IsEmpty));
    }

    internal static string Text(string key) => FlowBloxResourceUtil.GetLocalizedString(key, typeof(Resources.AiAssistantDebugView));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _disposed = true;
        _service.DebugStateChanged -= ServiceChanged;
    }
}
