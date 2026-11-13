using System;
using System.IO;
using FlowBlox.AvaloniaDesktop.Hosting;

namespace FlowBlox.Avalonia.IntegrationTests;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Emulate normal application startup for WPF pack URIs and application
        // metadata. WPF otherwise resolves /Icons/... against this runner executable.
        System.Reflection.Assembly.SetEntryAssembly(typeof(AvaloniaDesktop.App).Assembly);
        var invalidMode = args.Length == 3
            && args[2] != "--field-selection-only"
            && args[2] != "--launcher-only"
            && args[2] != "--quick-starts-only";
        if (args.Length < 2 || args.Length > 3 || invalidMode)
        {
            Console.Error.WriteLine("Usage: FlowBlox.Avalonia.IntegrationTests <results-directory> <example-project.fbprj> [--field-selection-only|--launcher-only|--quick-starts-only]");
            return 2;
        }

        var directory = Path.GetFullPath(args[0]);
        var sourceProjectPath = Path.GetFullPath(args[1]);
        if (!File.Exists(sourceProjectPath))
        {
            Console.Error.WriteLine($"Project not found: {sourceProjectPath}");
            return 2;
        }

        try
        {
            Directory.CreateDirectory(directory);
            // Prevent a failed startup from being mistaken for a previous successful run.
            var reportPath = Path.Combine(directory, "smoke-result.json");
            if (File.Exists(reportPath)) File.Delete(reportPath);
            var passed = false;
            var exitCode = global::FlowBlox.AvaloniaDesktop.Program.Run(Array.Empty<string>(), new DesktopStartupOptions
            {
                WorkspaceDirectory = directory,
                EnableInteractiveStartup = false,
                OnStarted = async window => passed = await new WorkspaceIntegrationChecks(window).RunAsync(
                    directory, sourceProjectPath,
                    fieldSelectionOnly: args.Length == 3 && args[2] == "--field-selection-only",
                    launcherOnly: args.Length == 3 && args[2] == "--launcher-only",
                    quickStartsOnly: args.Length == 3 && args[2] == "--quick-starts-only")
            });
            return exitCode != 0 ? exitCode : passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
