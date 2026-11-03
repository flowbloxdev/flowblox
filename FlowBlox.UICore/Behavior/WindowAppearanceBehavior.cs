using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlowBlox.UICore.Behavior;

/// <summary>
/// Requests the native Windows 11 rounded corners without replacing window chrome.
/// </summary>
public static class WindowAppearanceBehavior
{
    private const int WindowCornerPreferenceAttribute = 33;
    private const int DefaultCorners = 0;
    private const int RoundedCorners = 2;

    public static readonly DependencyProperty UseRoundedCornersProperty = DependencyProperty.RegisterAttached(
        "UseRoundedCorners", typeof(bool), typeof(WindowAppearanceBehavior), new PropertyMetadata(false, OnUseRoundedCornersChanged));

    public static void SetUseRoundedCorners(DependencyObject target, bool value) 
        => target.SetValue(UseRoundedCornersProperty, value);

    public static bool GetUseRoundedCorners(DependencyObject target) 
        => (bool)target.GetValue(UseRoundedCornersProperty);

    private static void OnUseRoundedCornersChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Window window)
            return;

        if ((bool)args.NewValue)
        {
            window.SourceInitialized += ApplyCorners;
            window.Loaded += ApplyCorners;
            window.StateChanged += ApplyCorners;
            window.Closed += OnClosed;
        }
        else
        {
            Detach(window);
        }
        Apply(window);
    }

    private static void ApplyCorners(object? sender, EventArgs args) => Apply((Window)sender!);
    private static void OnClosed(object? sender, EventArgs args) => Detach((Window)sender!);

    private static void Detach(Window window)
    {
        window.SourceInitialized -= ApplyCorners;
        window.Loaded -= ApplyCorners;
        window.StateChanged -= ApplyCorners;
        window.Closed -= OnClosed;
    }

    private static void Apply(Window window)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var preference = GetUseRoundedCorners(window) ? RoundedCorners : DefaultCorners;
        _ = DwmSetWindowAttribute(handle, WindowCornerPreferenceAttribute, ref preference, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int size);
}
