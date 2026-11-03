using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Avalonia.Input;

namespace FlowBlox.AppWindow.Contents;

public partial class WpfPanel
{
    private bool _transferringFocus;

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    private void AttachWpfFocusHandlers()
    {
        // The host itself is the keyboard target for clicks on non-focusable canvas areas.
        if (Child.ReadLocalValue(UIElement.FocusableProperty) == DependencyProperty.UnsetValue)
            Child.SetCurrentValue(UIElement.FocusableProperty, true);
        Child.AddHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(WpfPreviewMouseDown), true);
        Child.AddHandler(System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(WpfGotKeyboardFocus), true);
    }

    private void DetachWpfFocusHandlers()
    {
        Child.RemoveHandler(System.Windows.Input.Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(WpfPreviewMouseDown));
        Child.RemoveHandler(System.Windows.Input.Keyboard.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(WpfGotKeyboardFocus));
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        TransferFocusToWpf();
    }

    private void SynchronizeDockFocus() => AppWindow.Instance.NotifyHostedPanelFocused(this);

    private void TransferFocusToWpf()
    {
        if (_transferringFocus || IsDisposed || _source == null) return;
        _transferringFocus = true;
        try
        {
            SynchronizeDockFocus();
            // First update Avalonia's logical focus; then hand the Win32 focus back
            // to HwndSource, since focusing an Avalonia control can focus its window.
            Focus(NavigationMethod.Pointer);
            var focusedWindow = GetFocus();
            if (focusedWindow != Handle && !IsChild(Handle, focusedWindow)) SetFocus(Handle);
            if (!Child.IsKeyboardFocusWithin)
                Keyboard.Focus(Child);
        }
        finally 
        { 
            _transferringFocus = false; 
        }
    }

    private void WpfGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_transferringFocus && e.NewFocus is DependencyObject focused &&
            PresentationSource.FromDependencyObject(focused) == _source)
            TransferFocusToWpf();
    }

    private void WpfPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // WPF popups own separate HWNDs and manage their own keyboard focus.
        if (e.OriginalSource is not DependencyObject clicked ||
            PresentationSource.FromDependencyObject(clicked) != _source) return;

        TransferFocusToWpf();
        // Keep the mouse event unhandled so selection, caret placement and dragging
        // still run in the clicked WPF control.
    }

}
