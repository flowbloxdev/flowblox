using System.ComponentModel;
using System.Globalization;
using FlowBlox.AIAssistant.Models;
using FlowBlox.UICore.Commands;

namespace FlowBlox.UICore.ViewModels;

/// <summary>
/// Combines message kinds and origins; an empty group leaves that dimension unrestricted.
/// </summary>
public sealed class AiAssistantSystemMessageFilters : INotifyPropertyChanged
{
    private bool _updating;

    public IReadOnlyList<AiAssistantDebugFilterOption> Kinds { get; } = Enum.GetValues<AssistantSystemMessageKind>()
        .Select(kind => new AiAssistantDebugFilterOption(kind.ToString()))
        .ToArray();
    public IReadOnlyList<AiAssistantDebugFilterOption> Origins { get; } =
    [
        new("Static"),
        new("Dynamic")
    ];

    public RelayCommand SelectAllCommand { get; }
    public RelayCommand ClearCommand { get; }
    public string KindSummary => Summary("FilterKinds", Kinds);
    public string OriginSummary => Summary("FilterOrigins", Origins);

    public AiAssistantSystemMessageFilters()
    {
        SelectAllCommand = new RelayCommand(() => SetAll(true));
        ClearCommand = new RelayCommand(() => SetAll(false));
        foreach (var option in Kinds.Concat(Origins))
            option.PropertyChanged += OptionChanged;
    }

    public bool Includes(AssistantSystemMessage message)
    {
        return Matches(Kinds, message.Kind.ToString()) &&
            Matches(Origins, message.IsRuntimeBound ? "Dynamic" : "Static");
    }

    private static bool Matches(IReadOnlyList<AiAssistantDebugFilterOption> options, string key)
    {
        return !options.Any(option => option.IsSelected) ||
            options.Any(option => option.Key == key && option.IsSelected);
    }

    private static string Summary(string labelKey, IReadOnlyList<AiAssistantDebugFilterOption> options)
    {
        var selected = options.Count(option => option.IsSelected);
        var value = selected == 0 || selected == options.Count
            ? AiAssistantDebugViewModel.Text("FilterAll")
            : selected.ToString(CultureInfo.CurrentCulture);
        return AiAssistantDebugViewModel.Text(labelKey) + ": " + value;
    }

    private void SetAll(bool selected)
    {
        _updating = true;
        foreach (var option in Kinds.Concat(Origins))
            option.IsSelected = selected;
        _updating = false;
        NotifyChanged();
    }

    private void OptionChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!_updating)
            NotifyChanged();
    }

    private void NotifyChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(KindSummary)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginSummary)));
        FiltersChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? FiltersChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
}
