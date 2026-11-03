using System;
using System.Runtime.InteropServices;
using Avalonia.Input;

namespace FlowBlox.AvaloniaDesktop.Interop.Windows;

/// <summary>Translates native messages at the WPF hosting boundary into Avalonia key types.</summary>
internal static class HostedKeyboard
{
    public static Key KeyFromVirtualKey(int key)
    {
        var wpf = System.Windows.Input.KeyInterop.KeyFromVirtualKey(key);
        return Enum.TryParse<Key>(wpf.ToString(), out var result) ? result : Key.None;
    }

    public static KeyModifiers Modifiers =>
        (IsDown(0x11) ? KeyModifiers.Control : KeyModifiers.None) |
        (IsDown(0x10) ? KeyModifiers.Shift : KeyModifiers.None) |
        (IsDown(0x12) ? KeyModifiers.Alt : KeyModifiers.None) |
        (IsDown(0x5B) || IsDown(0x5C) ? KeyModifiers.Meta : KeyModifiers.None);

    private static bool IsDown(int key) => (GetKeyState(key) & 0x8000) != 0;
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);
}
