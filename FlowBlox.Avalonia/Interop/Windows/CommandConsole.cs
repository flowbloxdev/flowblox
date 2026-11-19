using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace FlowBlox.AvaloniaDesktop.Interop.Windows;

internal static class CommandConsole
{
    public static void Attach()
    {
        var output = GetStdHandle(-11);
        if (output == IntPtr.Zero || output == new IntPtr(-1))
        {
            if (AttachConsole(uint.MaxValue))
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            }
        }
        Console.OutputEncoding = new UTF8Encoding(false);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int standardHandle);
}
