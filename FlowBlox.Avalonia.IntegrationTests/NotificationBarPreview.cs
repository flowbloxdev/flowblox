using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using FlowBlox.AvaloniaDesktop.Resources;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.DependencyInjection;
using FlowBlox.Core.Util.Resources;

namespace FlowBlox.Avalonia.IntegrationTests;

internal sealed partial class WorkspaceIntegrationChecks
{
    private async Task CaptureNotificationBarPreviewAsync(string directory)
    {
        var notifications = FlowBloxServiceLocator.Instance.GetService<IAppNotificationService>();
        const string notificationId = "integration.notification.preview";
        notifications.Publish(new AppNotification
        {
            Id = notificationId,
            Message = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateAvailable_Message", typeof(FlowBloxMainUITexts)),
            ActionText = FlowBloxResourceUtil.GetLocalizedString("AppWindow_Notification_UpdateAvailable_Action_Text", typeof(FlowBloxMainUITexts))
        });
        try
        {
            _window.Activate();
            await Task.Delay(200);
            var bar = _window.FindControl<Border>("NotificationBar");
            using var screenshot = new RenderTargetBitmap(
                new PixelSize((int)bar.Bounds.Width, (int)bar.Bounds.Height),
                new Vector(96, 96));
            screenshot.Render(bar);
            screenshot.Save(Path.Combine(directory, "notification-bar.png"));
        }
        finally
        {
            notifications.Dismiss(notificationId);
        }
    }
}
