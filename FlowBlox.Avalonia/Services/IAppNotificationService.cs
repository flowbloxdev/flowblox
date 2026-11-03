using System;
using System.Collections.Generic;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public interface IAppNotificationService
    {
        event EventHandler NotificationsChanged;

        AppNotification GetCurrentNotification();

        IReadOnlyCollection<AppNotification> GetActiveNotifications();

        void Publish(AppNotification notification);

        void Dismiss(string notificationId);

        void RemoveExpiredNotifications();
    }
}
