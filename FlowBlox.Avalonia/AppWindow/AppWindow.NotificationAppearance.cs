using Avalonia.Controls;
using Avalonia.Media;
using FlowBlox.AvaloniaDesktop.Services;
using FlowBlox.Core.Attributes;

namespace FlowBlox.AppWindow;

public partial class AppWindow
{
    private void ApplyNotificationAppearance(AppNotification notification)
    {
        var (background, accent, foreground, icon) = notification?.NotificationType switch
        {
            NotificationType.Error => ("#492B2E", "#E48383", "#FFE8E8", "⚠"),
            NotificationType.Warning => ("#463D26", "#E4BC68", "#FFF2D3", "⚠"),
            _ => ("#263B46", "#568FA5", "#EDF5F8", "ⓘ")
        };

        NotificationBar.Background = Brush.Parse(background);
        NotificationBar.BorderBrush = Brush.Parse(accent);
        NotificationMessage.Foreground = Brush.Parse(foreground);
        NotificationAction.Background = Brush.Parse(background);
        NotificationAction.BorderBrush = Brush.Parse(accent);
        NotificationAction.Foreground = Brush.Parse(foreground);
        var notificationIcon = this.FindControl<TextBlock>("NotificationIcon");
        notificationIcon.Text = icon;
        notificationIcon.Foreground = Brush.Parse(accent);
    }
}
