using System.Diagnostics;
using System.Text.Json;
using FlowBlox.Core.Constants;
using FlowBlox.Core.Models.WebServer;

namespace FlowBlox.Core.Util;

/// <summary>Controls only the recorded web-server process in the current Windows account.</summary>
public static class WebServerProcess
{
    private static string IdentityFile => Path.Combine(GlobalPaths.UserDataDirectory, "webserver", "process.json");

    public static Process? GetActiveProcess()
    {
        if (!File.Exists(IdentityFile))
            return null;
        Process? process = null;
        try
        {
            var identity = JsonSerializer.Deserialize<WebServerProcessIdentity>(File.ReadAllText(IdentityFile));
            if (identity == null)
                return null;
            process = Process.GetProcessById(identity.ProcessId);
            if (!process.HasExited && process.StartTime.ToUniversalTime() == identity.StartedUtc
                && string.Equals(process.ProcessName, "FlowBlox.WebServer", StringComparison.OrdinalIgnoreCase))
                return process;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or JsonException or System.ComponentModel.Win32Exception) { }
        process?.Dispose();
        return null;
    }

    public static void RecordCurrentProcess()
    {
        using var process = Process.GetCurrentProcess();
        Record(process);
    }

    private static void Record(Process process)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(IdentityFile)!);
        var temporary = IdentityFile + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new WebServerProcessIdentity(process.Id, process.StartTime.ToUniversalTime())));
        File.Move(temporary, IdentityFile, overwrite: true);
    }

    public static void Start(string executable)
    {
        using var active = GetActiveProcess();
        if (active != null)
            return;
        var configuration = WebServerConfigurationStore.Load();
        var errors = FlowBlox.Core.Validations.FlowBloxComponentValidator.Validate(configuration);
        if (errors.Count > 0)
            throw new System.ComponentModel.DataAnnotations.ValidationException(string.Join(Environment.NewLine, errors));
        if (!File.Exists(executable))
            throw new FileNotFoundException("FlowBlox.WebServer executable is missing.", executable);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("--data-directory");
        start.ArgumentList.Add(GlobalPaths.UserDataDirectory);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Web server did not start.");
        // The server records itself once it owns its single-instance lock.
    }

    public static void Stop()
    {
        using var process = GetActiveProcess();
        if (process == null)
            return;
        // Do not kill descendant runner/browser processes: active executions have their own stop action.
        process.Kill(entireProcessTree: false);
        if (!process.WaitForExit(5000))
            throw new TimeoutException("The web server did not stop.");
    }
}
