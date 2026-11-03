using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CommandLine;
using FlowBlox.AvaloniaDesktop.CommandLine;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Logging;
using FlowBlox.AvaloniaDesktop.Hosting;

namespace FlowBlox.AvaloniaDesktop;

public partial class App : Application
{
    internal static DesktopStartupOptions StartupOptions { get; set; } = new();
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnMainWindowClose;
            var splash = new SplashWindow();
            desktop.MainWindow = splash;
            splash.Opened += async (_, _) =>
            {
                try
                {
                    await splash.PlayIntroAsync();
                    await Task.Run(AppWindow.AppWindow.InitApp);
                    await Task.Delay(500);
                    if (StartupOptions.EnableInteractiveStartup) await new FlowBloxAutoLoginExecutor().TryAutoLoginAsync();
                    var window = AppWindow.AppWindow.Instance;
                    new Parser().ParseArguments<Options>(desktop.Args ?? Array.Empty<string>()).WithParsed(options =>
                    {
                        window.SetProjectFile(options.ProjectFile);
                        window.SetProjectSpaceGuid(options.ProjectSpaceGuid);
                    });
                    desktop.MainWindow = window;
                    window.Show();
                    splash.Complete();
                    if (StartupOptions.OnStarted != null) await StartupOptions.OnStarted(window);
                }
                catch (Exception ex)
                {
                    FlowBloxLogManager.Instance.GetLogger().Error("Application initialization failed.", ex);
                    if (StartupOptions.EnableInteractiveStartup) splash.ShowError(ex);
                    else desktop.Shutdown(1);
                }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
