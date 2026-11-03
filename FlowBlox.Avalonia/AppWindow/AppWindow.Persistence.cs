using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Dock.Avalonia.Controls;
using FlowBlox.AvaloniaDesktop.Docking;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private WorkspaceLayoutObserver _layoutObserver;
    private IDisposable _pendingLayoutSave;
    internal bool IsWorkspaceSavePending => _pendingLayoutSave != null;

    private void InitializeWorkspacePersistence()
    {
        Workspace.PropertyChanged += WorkspaceLayoutChanged;
        PropertyChanged += WindowPersistenceChanged;
    }

    private void WorkspaceLayoutChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != DockControl.LayoutProperty) return;
        _pendingLayoutSave?.Dispose();
        _pendingLayoutSave = null;
        _layoutObserver?.Dispose();
        _layoutObserver = Workspace.Layout == null ? null :
            new WorkspaceLayoutObserver(Workspace.Layout, RequestLayoutSave);
        if (_layoutReady) RequestLayoutSave();
    }

    private void WindowPersistenceChanged(object sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WidthProperty || e.Property == HeightProperty || e.Property == WindowStateProperty)
            RequestLayoutSave();
    }

    private void RequestLayoutSave()
    {
        _pendingLayoutSave?.Dispose();
        // One-shot debounce, scheduled only by actual changes. There is no idle polling.
        _pendingLayoutSave = DispatcherTimer.RunOnce(SaveLayout, TimeSpan.FromMilliseconds(400));
    }

    private void DisposeWorkspacePersistence()
    {
        Workspace.PropertyChanged -= WorkspaceLayoutChanged;
        PropertyChanged -= WindowPersistenceChanged;
        _pendingLayoutSave?.Dispose();
        _pendingLayoutSave = null;
        _layoutObserver?.Dispose();
        _layoutObserver = null;
    }
}
