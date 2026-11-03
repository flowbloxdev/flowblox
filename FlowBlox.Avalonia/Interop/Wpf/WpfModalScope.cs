using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Avalonia.Controls;

namespace FlowBlox.AvaloniaDesktop.Interop.Wpf;

/// <summary>Preserves WPF modality while an Avalonia dialog is open.</summary>
internal sealed class WpfModalScope : IDisposable
{
    private readonly List<IntPtr> _disabled = new();
    private readonly IntPtr _owner;
    private readonly bool _ownerWasEnabled;
    private readonly System.Windows.Window _activeWindow;

    public WpfModalScope(Window owner)
    {
        if (!OperatingSystem.IsWindows()) return;
        _owner = owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        _ownerWasEnabled = IsWindowEnabled(_owner);
        var windows = System.Windows.Application.Current?.Windows.Cast<System.Windows.Window>()
            .Where(window => window.IsVisible).ToArray() ?? Array.Empty<System.Windows.Window>();
        _activeWindow = windows.LastOrDefault(window => window.IsActive);
        foreach (var window in windows)
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero || !IsWindowEnabled(handle)) continue;
            _disabled.Add(handle);
            EnableWindow(handle, false);
        }
    }

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        foreach (var handle in _disabled)
            if (IsWindow(handle)) EnableWindow(handle, true);
        // Avalonia must not enable a main window that a WPF modal dialog disabled.
        if (_owner != IntPtr.Zero && IsWindow(_owner)) EnableWindow(_owner, _ownerWasEnabled);
        if (_activeWindow?.IsVisible == true) _activeWindow.Activate();
    }

    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr hwnd, bool enabled);
}
