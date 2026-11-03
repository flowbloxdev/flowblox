using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Media.Imaging;

namespace FlowBlox.AvaloniaDesktop.ViewModels;

public sealed class MenuEntry : INotifyPropertyChanged, ICommand
{
    private bool _enabled = true;
    private bool _checked;
    public string Text { get; set; }
    public object Tag { get; set; }
    public Bitmap Icon { get; set; }
    public Avalonia.Input.KeyGesture Gesture { get; set; }
    public bool IsSeparator { get; set; }
    public ObservableCollection<MenuEntry> DropDownItems { get; } = new();
    public bool Enabled { get => _enabled; set { _enabled = value; PropertyChanged?.Invoke(this, new(nameof(Enabled))); CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
    public bool IsChecked { get => _checked; set { _checked = value; PropertyChanged?.Invoke(this, new(nameof(IsChecked))); } }
    public event EventHandler Click;
    public event PropertyChangedEventHandler PropertyChanged;
    public event EventHandler CanExecuteChanged;
    public bool CanExecute(object parameter) => Enabled;
    public void Execute(object parameter) { if (Enabled) Click?.Invoke(this, EventArgs.Empty); }
}
