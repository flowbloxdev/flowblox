using FlowBlox.Core.Constants;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using FlowBlox.Core.Logging;
using FlowBlox.Core.Util;
using FlowBlox.AvaloniaDesktop.Interop.Windows;
using FlowBlox.AvaloniaDesktop.Hosting;

namespace FlowBlox.AvaloniaDesktop;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => Run(args, new DesktopStartupOptions());

    internal static int Run(string[] args, DesktopStartupOptions options)
    {
        App.StartupOptions = options;
        var fontDirectory = Path.Combine(AppContext.BaseDirectory, "data", "fonts");
        if (Directory.Exists(fontDirectory))
            foreach (var file in Directory.EnumerateFiles(fontDirectory).Where(x => x.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)))
                try { FontInstaller.InstallFontForCurrentUser(file); }
                catch (Exception ex) { FlowBloxLogManager.Instance.GetLogger().Error("Unable to install font.", ex); }

        var culture = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.UICulture)?.Value;
        if (!string.IsNullOrWhiteSpace(culture))
            try { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo(culture); }
            catch (CultureNotFoundException ex) { FlowBloxLogManager.Instance.GetLogger().Error("Invalid UI culture.", ex); }

        // Retain WPF resources and dispatcher for the existing Fachansichten.
        _ = new System.Windows.Application { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
        Dock.Settings.DockSettings.ShowDockablePreviewOnDrag = false;
        // Native WPF child windows would otherwise cover Avalonia's in-tree drop indicators.
        Dock.Settings.DockSettings.UseFloatingDockAdorner = true;
        var exitCode = AppBuilder.Configure<App>().UsePlatformDetect()
            .With(new Win32PlatformOptions { ShouldRenderOnUIThread = true, CompositionMode = new[] { Win32CompositionMode.RedirectionSurface } })
            .LogToTrace().StartWithClassicDesktopLifetime(args);
        System.Windows.Application.Current?.Shutdown();
        return exitCode;
    }
}
