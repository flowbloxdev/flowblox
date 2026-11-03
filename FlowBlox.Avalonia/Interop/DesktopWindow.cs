using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace FlowBlox.AvaloniaDesktop.Interop;

public class DesktopWindow : Window
{
    public IntPtr Handle => TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    public string Text { get => Title; set => Title = value; }
    public bool InvokeRequired => !Dispatcher.UIThread.CheckAccess();
    public void Invoke(Delegate action, params object[] args) => Dispatcher.UIThread.Invoke(() => action.DynamicInvoke(args));
    public void BeginInvoke(Delegate action, params object[] args) => Dispatcher.UIThread.Post(() => action.DynamicInvoke(args));
}
