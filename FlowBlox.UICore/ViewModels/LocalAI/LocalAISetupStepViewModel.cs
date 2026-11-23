using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using MahApps.Metro.IconPacks;

namespace FlowBlox.UICore.ViewModels.LocalAI;

public sealed class LocalAISetupStepViewModel : INotifyPropertyChanged
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public bool CanOverwrite { get; init; }
    public bool OverwriteExisting { get; set; }
    public PackIconMaterialKind Icon { get; private set; } = PackIconMaterialKind.ClockOutline;
    public Brush Color { get; private set; } = Brushes.SlateGray;

    public void Update(string status, PackIconMaterialKind icon, Brush color)
    {
        Status = status;
        Icon = icon;
        Color = color;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Color));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
