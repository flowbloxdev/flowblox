using System;
using FlowBlox.Core.Constants;
using Microsoft.Win32;

namespace FlowBlox.AvaloniaDesktop.Services;

internal static class WebServerAutostart
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FlowBlox.WebServer";

    public static bool Enabled
    {
        get { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue(ValueName) is string; }
    }

    public static void SetEnabled(string executable, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
        if (enabled)
        {
            var script = "Start-Process -WindowStyle Hidden -FilePath '" + executable.Replace("'", "''")
                + "' -ArgumentList '--data-directory \"" + GlobalPaths.UserDataDirectory.TrimEnd('\\').Replace("'", "''") + "\"'";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            key.SetValue(ValueName, "powershell.exe -NoProfile -WindowStyle Hidden -EncodedCommand " + encoded, RegistryValueKind.String);
        }
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
