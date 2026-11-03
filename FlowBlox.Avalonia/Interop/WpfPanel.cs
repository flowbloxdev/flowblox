using System;
using System.Windows;
using System.Windows.Interop;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace FlowBlox.AppWindow.Contents;

/// <summary>Windows migration boundary; the shell and docking remain Avalonia.</summary>
public partial class WpfPanel : NativeControlHost, IDisposable
{
    public WpfPanel()
    {
        Focusable = true;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch;
    }
    private HwndSource _source;
    private bool _loaded;
    protected UIElement Child { get; set; }
    public string Text { get; set; }
    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;
    public bool InvokeRequired => !Dispatcher.UIThread.CheckAccess();
    public bool IsDisposed { get; private set; }
    public bool IsHandleCreated => Handle != IntPtr.Zero;
    public bool IsHidden => !IsVisible;
    public bool HasWpfKeyboardFocus => Child?.IsKeyboardFocusWithin == true;
    internal System.Windows.FrameworkElement WpfContent => Child as System.Windows.FrameworkElement;
    public void Invoke(Delegate action, params object[] args) => Dispatcher.UIThread.Invoke(() => action.DynamicInvoke(args));
    public void BeginInvoke(Delegate action, params object[] args) => Dispatcher.UIThread.Post(() => { if (!IsDisposed) action.DynamicInvoke(args); });
    public void Activate() => AppWindow.Instance.ActivatePanel(this);
    public void Show() => Activate();

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var parameters = new HwndSourceParameters(GetType().Name)
        {
            ParentWindow = parent.Handle,
            WindowStyle = unchecked((int)0x56000000), // child, visible, clip siblings and children
            Width = Math.Max(1, (int)Bounds.Width),
            Height = Math.Max(1, (int)Bounds.Height)
        };
        _source = new HwndSource(parameters) { RootVisual = Child };
        // Match the existing ManagedObjects splitter beneath transparent WPF areas.
        // Explicit backgrounds inside the shared views remain in control of their surfaces.
        _source.CompositionTarget.BackgroundColor = System.Windows.Media.Color.FromRgb(0xE2, 0xE8, 0xEF);
        _source.AddHook(FilterKeyboardMessage);
        AttachWpfFocusHandlers();
        if (!_loaded) { _loaded = true; OnLoad(EventArgs.Empty); }
        return new PlatformHandle(_source.Handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_source == null) return;
        DetachWpfFocusHandlers();
        _source.RemoveHook(FilterKeyboardMessage);
        _source.RootVisual = null;
        _source.Dispose();
        _source = null;
    }

    private IntPtr FilterKeyboardMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) // WM_MOUSEACTIVATE: activate without consuming the click.
        {
            TransferFocusToWpf();
            handled = true;
            return new IntPtr(1); // MA_ACTIVATE
        }
        if (message is 0x0201 or 0x0204 or 0x0207 or 0x020B) TransferFocusToWpf();
        if (message == 0x0007) SynchronizeDockFocus(); // WM_SETFOCUS
        if (!handled && message == 0x100)
            handled = AppWindow.Instance.ProcessNativeKey(wParam.ToInt32());
        return IntPtr.Zero;
    }

    protected virtual void OnLoad(EventArgs e) { }
    protected virtual void Dispose(bool disposing) { }
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        Dispose(true);
        if (_source != null) 
            DestroyNativeControlCore(new PlatformHandle(_source.Handle, "HWND"));
    }
}
