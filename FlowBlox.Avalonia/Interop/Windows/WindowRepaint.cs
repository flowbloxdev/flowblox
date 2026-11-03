using System;
using System.Runtime.InteropServices;

namespace FlowBlox.AvaloniaDesktop.Interop.Windows;

internal static class WindowRepaint
{
    /// <summary>
    /// Invalidates the complete client surface after native child windows have been hidden.
    /// </summary>
    internal static void Request(IntPtr handle)
    {
        if (OperatingSystem.IsWindows() && handle != IntPtr.Zero)
            InvalidateRect(handle, IntPtr.Zero, false);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(IntPtr handle, IntPtr rectangle, [MarshalAs(UnmanagedType.Bool)] bool erase);
}
