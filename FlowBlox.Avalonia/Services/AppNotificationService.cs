using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowBlox.AvaloniaDesktop.Services
{
    public class AppNotificationService : IAppNotificationService
    {
        private readonly object _lock = new object();
        private readonly List<AppNotification> _notifications = [];
        private readonly System.Threading.Timer _expirationTimer;

        public AppNotificationService()
        {
            _expirationTimer = new System.Threading.Timer(
                ExpirationTimerElapsed,
                null,
                System.Threading.Timeout.InfiniteTimeSpan,
                System.Threading.Timeout.InfiniteTimeSpan);
        }

        public event EventHandler NotificationsChanged;

        public AppNotification GetCurrentNotification()
        {
            lock (_lock)
            {
                RemoveExpiredNotificationsInternal();
                ScheduleExpirationCheckLocked();
                return _notifications
                    .OrderByDescending(x => x.CreatedUtc)
                    .FirstOrDefault();
            }
        }

        public IReadOnlyCollection<AppNotification> GetActiveNotifications()
        {
            lock (_lock)
            {
                RemoveExpiredNotificationsInternal();
                ScheduleExpirationCheckLocked();
                return _notifications
                    .OrderByDescending(x => x.CreatedUtc)
                    .ToList()
                    .AsReadOnly();
            }
        }

        public void Publish(AppNotification notification)
        {
            if (notification == null)
                return;

            lock (_lock)
            {
                RemoveExpiredNotificationsInternal();
                _notifications.RemoveAll(x => x.Id == notification.Id);
                _notifications.Add(notification);
                ScheduleExpirationCheckLocked();
            }

            NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Dismiss(string notificationId)
        {
            if (string.IsNullOrWhiteSpace(notificationId))
                return;

            var removed = false;

            lock (_lock)
            {
                removed = _notifications.RemoveAll(x => x.Id == notificationId) > 0;
                ScheduleExpirationCheckLocked();
            }

            if (removed)
                NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }

        public void RemoveExpiredNotifications()
        {
            var removed = false;

            lock (_lock)
            {
                removed = RemoveExpiredNotificationsInternal();
                ScheduleExpirationCheckLocked();
            }

            if (removed)
                NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }

        private bool RemoveExpiredNotificationsInternal()
        {
            return _notifications.RemoveAll(x => x.IsExpired) > 0;
        }

        private void ExpirationTimerElapsed(object state)
        {
            bool removed;
            lock (_lock)
            {
                removed = RemoveExpiredNotificationsInternal();
                ScheduleExpirationCheckLocked();
            }

            if (removed)
                NotificationsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ScheduleExpirationCheckLocked()
        {
            var nextExpiration = _notifications
                .Where(x => x.ExpiresUtc.HasValue)
                .Select(x => x.ExpiresUtc.Value)
                .DefaultIfEmpty(DateTimeOffset.MaxValue)
                .Min();

            if (nextExpiration == DateTimeOffset.MaxValue)
            {
                _expirationTimer.Change(
                    System.Threading.Timeout.InfiniteTimeSpan,
                    System.Threading.Timeout.InfiniteTimeSpan);
                return;
            }

            var dueTime = nextExpiration - DateTimeOffset.UtcNow;
            if (dueTime < TimeSpan.Zero)
                dueTime = TimeSpan.Zero;

            _expirationTimer.Change(dueTime, System.Threading.Timeout.InfiniteTimeSpan);
        }
    }
}
