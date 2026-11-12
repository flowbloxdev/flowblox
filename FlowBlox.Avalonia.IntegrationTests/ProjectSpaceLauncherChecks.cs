using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Attributes;
using FlowBlox.Core.Authentication;
using FlowBlox.Core.Constants;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.ExternalServices.FlowBloxWebApi.Models;
using FlowBlox.Core.Util;
using FlowBlox.Core.Util.Resources;
using Texts = FlowBlox.UICore.Resources.ProjectLauncher;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CheckOwnProjectSpaceShortcutsAsync(Action<bool, string> check, string directory)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        var option = FlowBloxOptions.GetOptionInstance().GetOption(OptionsConstants.ApiProjectServiceBaseUrl);
        var previousEndpoint = option.Value;
        var accounts = FlowBloxAccountManager.Instance;
        var notifications = FlowBloxServiceLocator.Instance.GetService<IAppNotificationService>();
        var ownTitle = FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.OwnProjects), typeof(Texts));
        try
        {
            _window.Width = 1000;
            _window.Height = 700;
            _window.Position = new global::Avalonia.PixelPoint(40, 40);
            _window.Topmost = true;
            _window.Activate();
            await Task.Delay(150);
            option.Value = endpoint;
            await _window.RefreshOwnProjectShortcutsAsync();
            check(!listener.Pending() && !_window.ShortcutSections.Any(section => section.Title == ownTitle),
                "Without credentials the launcher neither calls Project Space nor displays an own-project box");

            accounts.SetSession(endpoint, new FbUserData { UserName = "launcher-test" }, null);
            await _window.RefreshOwnProjectShortcutsAsync();
            check(!listener.Pending() && !notifications.GetActiveNotifications().Any(item => item.Id == "launcher.projectspace.load.failed"),
                "An account without a token does not trigger an API request or an error notification");
            accounts.SetUserToken(endpoint, "launcher-test-token");
            var response = new TaskCompletionSource<string>();
            var serve = RespondToLauncherProjectRequestAsync(listener, response.Task, check);
            var refresh = _window.RefreshOwnProjectShortcutsAsync();
            await Task.Delay(100);
            check(!refresh.IsCompleted && !_window.ShortcutSections.Any(section => section.Title == ownTitle),
                "Pending Project Space requests leave existing boxes usable without showing an empty own-project box");
            response.SetResult("[{\"Guid\":\"a1111111-1111-1111-1111-111111111111\",\"Name\":\"Owned online project\"}]");
            await Task.WhenAll(serve, refresh).WaitAsync(TimeSpan.FromSeconds(5));
            var owned = _window.ShortcutSections.Single(section => section.Title == ownTitle).Items.Single();
            check(owned.IsOnline && !owned.IsLocal && owned.Project.ApiEndpoint == endpoint,
                "Successful own-project lookup adds an independent online-only shortcut box");

            serve = RespondToLauncherProjectRequestAsync(listener, Task.FromResult("[]"), check);
            await Task.WhenAll(serve, _window.RefreshOwnProjectShortcutsAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            check(!_window.ShortcutSections.Any(section => section.Title == ownTitle),
                "An empty own-project response hides the box");

            response = new TaskCompletionSource<string>();
            serve = RespondToLauncherProjectRequestAsync(listener, response.Task, check);
            refresh = _window.RefreshOwnProjectShortcutsAsync();
            accounts.SetSession(endpoint, null, null);
            response.SetResult("[{\"Guid\":\"a1111111-1111-1111-1111-111111111111\",\"Name\":\"Stale project\"}]");
            await Task.WhenAll(serve, refresh).WaitAsync(TimeSpan.FromSeconds(5));
            check(!_window.ShortcutSections.Any(section => section.Title == ownTitle),
                "A late response after logout does not expose projects from the previous account");

            accounts.SetSession(endpoint, new FbUserData { UserName = "launcher-test" }, "launcher-test-token");
            serve = RespondToLauncherProjectRequestAsync(listener, Task.FromResult("API unavailable"), check);
            await Task.WhenAll(serve, _window.RefreshOwnProjectShortcutsAsync()).WaitAsync(TimeSpan.FromSeconds(5));
            var notification = notifications.GetCurrentNotification();
            var bar = _window.FindControl<Border>("NotificationBar");
            check(notification?.NotificationType == NotificationType.Error
                && notification.Message == FlowBloxResourceUtil.GetLocalizedString(nameof(Texts.OwnProjectsLoadFailed), typeof(Texts))
                && bar.IsVisible && ((ISolidColorBrush)bar.Background).Color == Color.Parse("#492B2E"),
                "A failed authenticated lookup shows a readable localized error in the red notification bar");
            await Task.Delay(150);
            using (var screenshot = CaptureDesktopWindow())
                screenshot.Save(Path.Combine(directory, "launcher-projectspace-error.png"));

            notifications.Publish(new AppNotification
            {
                Id = "launcher.projectspace.load.failed",
                Message = "Warning color check",
                NotificationType = NotificationType.Warning
            });
            check(((ISolidColorBrush)bar.Background).Color == Color.Parse("#463D26"),
                "The notification bar switches from red to amber for warnings");
            notifications.Publish(new AppNotification
            {
                Id = "launcher.projectspace.load.failed",
                Message = "Information color check"
            });
            check(((ISolidColorBrush)bar.Background).Color == Color.Parse("#263B46"),
                "The notification bar restores its informational color for normal messages");
            accounts.SetSession(endpoint, null, null);
            await _window.RefreshOwnProjectShortcutsAsync();
            check(!listener.Pending() && !notifications.GetActiveNotifications().Any(item => item.Id == "launcher.projectspace.load.failed"),
                "Logging out clears the lookup error and suppresses further API requests");
        }
        finally
        {
            accounts.SetSession(endpoint, null, null);
            option.Value = previousEndpoint;
            notifications.Dismiss("launcher.projectspace.load.failed");
            _window.Topmost = false;
        }
    }

    private static async Task RespondToLauncherProjectRequestAsync(
        TcpListener listener,
        Task<string> response,
        Action<bool, string> check)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true);
        var request = await reader.ReadLineAsync();
        var authorization = string.Empty;
        string header;
        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
        {
            if (header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                authorization = header;
        }
        check(request.Contains("mine=1") && authorization == "Authorization: Bearer launcher-test-token",
            "Own-project lookup uses the current account token and the API ownership filter");
        var body = Encoding.UTF8.GetBytes(await response);
        var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(headers);
        await stream.WriteAsync(body);
    }
}
