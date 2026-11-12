using System;
using FlowBlox.Core.Attributes;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public enum AppNotificationActionType
    {
        None = 0,
        DownloadUpdateInstaller = 1,
        InstallDownloadedUpdate = 2
    }

    public class AppNotification
    {
        public string Id { get; set; }

        public string Message { get; set; }

        public NotificationType NotificationType { get; set; } = NotificationType.None;

        public string ActionText { get; set; }

        public AppNotificationActionType ActionType { get; set; }

        public string ActionData { get; set; }

        public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;

        public DateTimeOffset? ExpiresUtc { get; set; }

        public bool IsExpired => ExpiresUtc.HasValue && DateTimeOffset.UtcNow >= ExpiresUtc.Value;
    }
}
