using System.ComponentModel;

namespace FlowBlox.UICore.ViewModels;

public sealed class AiAssistantDebugFilterOption(string key) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Key { get; } = key;
    public string Label => AiAssistantDebugViewModel.Text(Key);
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;

            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
