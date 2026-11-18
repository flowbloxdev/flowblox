using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using FlowBlox.AvaloniaDesktop.Interop.Wpf;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.AvaloniaDesktop.ViewModels;
using FlowBlox.Core;
using FlowBlox.Core.Logging;
using SkiaSharp;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using FlowBlox.UICore.ViewModels;
using FlowBlox.UICore.Views;
using Texts = FlowBlox.AvaloniaDesktop.Resources.FlowBloxMainUITexts;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private readonly MenuEntry _webStart = new(), _webStop = new(), _webRestart = new(), _webOpen = new(), _webAutostart = new();
    private readonly DispatcherTimer _webStatusTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _webOperation;
    private string WebServerExecutable => File.Exists(Path.Combine(AppContext.BaseDirectory, "FlowBlox.WebServer.exe"))
        ? Path.Combine(AppContext.BaseDirectory, "FlowBlox.WebServer.exe")
        : Path.Combine(AppContext.BaseDirectory, "WebServer", "FlowBlox.WebServer.exe");
    private static string WebText(string key) => FlowBloxResourceUtil.GetLocalizedString("WebServer_" + key, typeof(Texts));

    private void InitializeWebServerMenu()
    {
        var menu = new MenuEntry
        {
            Text = WebText("Menu"),
            Icon = CreateMenuSvgIcon(FlowBloxIcons.server_network, new SKColor(0x71, 0xC9, 0xEE))
        };
        var configuration = new MenuEntry { Text = WebText("Configuration") };
        _webStart.Text = WebText("Start"); _webStop.Text = WebText("Stop"); _webRestart.Text = WebText("Restart");
        _webOpen.Text = WebText("Open"); _webAutostart.Text = WebText("Autostart");
        _webOpen.Icon = CreateMenuSvgIcon(FlowBloxIcons.view_dashboard_outline, new SKColor(0x71, 0xC9, 0xEE));
        _webStart.Icon = CreateMenuSvgIcon(FlowBloxIcons.play, new SKColor(0x81, 0xC7, 0x84));
        _webStop.Icon = CreateMenuSvgIcon(FlowBloxIcons.stop, new SKColor(0xE9, 0x90, 0x89));
        _webRestart.Icon = CreateMenuSvgIcon(FlowBloxIcons.restart, new SKColor(0xE8, 0xBD, 0x6B));
        configuration.Icon = CreateMenuSvgIcon(FlowBloxIcons.cog_outline, new SKColor(0x69, 0xCB, 0xC4));
        _webAutostart.Icon = CreateMenuSvgIcon(FlowBloxIcons.power_settings, new SKColor(0xA8, 0xB6, 0xC5));

        _webStart.Click += async (_, _) => await ControlWebServerAsync("start");
        _webStop.Click += async (_, _) => await ControlWebServerAsync("stop");
        _webRestart.Click += async (_, _) => await ControlWebServerAsync("restart");
        _webOpen.Click += (_, _) => OpenWebServer();
        _webAutostart.Click += (_, _) =>
        {
            try { WebServerAutostart.SetEnabled(WebServerExecutable, !WebServerAutostart.Enabled); RefreshWebServerStatus(); }
            catch (Exception exception) { ShowWebServerError(exception); }
        };
        configuration.Click += (_, _) => ConfigureWebServer();
        foreach (var entry in new[] { _webOpen, _webStart, _webStop, _webRestart, configuration, _webAutostart }) menu.DropDownItems.Add(entry);
        mnItmMisc.DropDownItems.Insert(mnItmMisc.DropDownItems.Count - 1, menu);
        _webStatusTimer.Tick += (_, _) => RefreshWebServerStatus();
        Closed += (_, _) => _webStatusTimer.Stop();
        _webStatusTimer.Start();
        RefreshWebServerStatus();
    }

    private void RefreshWebServerStatus()
    {
        using var process = WebServerProcess.GetActiveProcess();
        var running = process != null;
        _webStart.Enabled = !_webOperation && !running;
        _webStop.Enabled = _webRestart.Enabled = _webOpen.Enabled = !_webOperation && running;
        _webAutostart.IsChecked = WebServerAutostart.Enabled;
    }

    private async Task ControlWebServerAsync(string action)
    {
        _webOperation = true; RefreshWebServerStatus();
        try
        {
            await Task.Run(async () =>
            {
                if (action is "stop" or "restart") WebServerProcess.Stop();
                if (action is "start" or "restart")
                {
                    WebServerProcess.Start(WebServerExecutable);
                    for (var attempt = 0; attempt < 30; attempt++)
                    {
                        await Task.Delay(200);
                        using var process = WebServerProcess.GetActiveProcess();
                        if (process != null) return;
                    }
                    throw new InvalidOperationException(WebText("StartFailed"));
                }
            });
        }
        catch (Exception exception) { ShowWebServerError(exception); }
        finally { _webOperation = false; RefreshWebServerStatus(); }
    }

    private void ConfigureWebServer()
    {
        try
        {
            var configuration = WebServerConfigurationStore.Load();
            var window = new PropertyWindow(new PropertyWindowArgs(configuration, readOnly: false, canSave: true, detached: true))
            { Title = WebText("Configuration"), Width = 880, Height = 720 };
            WpfDialogHost.ShowDialog(window, this);
            if (window.DialogResult != true) return;
            WebServerConfigurationStore.Save(configuration);
            DesktopDialogs.Messages.Show(WebText("Saved"), WebText("Menu"), MessageBoxButtons.Ok, MessageBoxIcon.Information);
        }
        catch (Exception exception) { ShowWebServerError(exception); }
    }

    private void OpenWebServer()
    {
        try
        {
            var configuration = WebServerConfigurationStore.Load();
            var address = configuration.BindAddress is "0.0.0.0" or "::" ? "localhost" : configuration.BindAddress;
            if (address.Contains(':')) address = "[" + address + "]";
            var scheme = string.IsNullOrWhiteSpace(configuration.CertificateFile) ? "http" : "https";
            Process.Start(new ProcessStartInfo($"{scheme}://{address}:{configuration.Port}/") { UseShellExecute = true });
        }
        catch (Exception exception) { ShowWebServerError(exception); }
    }

    private static void ShowWebServerError(Exception exception)
    {
        FlowBloxLogManager.Instance.GetLogger().Error("Web server desktop operation failed.", exception);
        DesktopDialogs.Messages.Show(WebText("Error") + Environment.NewLine + exception.Message, WebText("Menu"), MessageBoxButtons.Ok, MessageBoxIcon.Error);
    }
}
