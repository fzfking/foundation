using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Unity.Notifications;

namespace Foundation.GameFramework.Notifications.Mobile
{
    public sealed class MobileNotificationService : IPushNotificationService
    {
        private readonly NotificationSpacingQueue _spacing;
        private readonly string _androidChannelId;
        private readonly string _androidChannelName;
        private readonly string _androidChannelDescription;
        private bool _initialized;

        public MobileNotificationService(
            string androidChannelId = "default",
            string androidChannelName = "Notifications",
            string androidChannelDescription = "General notifications",
            TimeSpan? minimumSpacing = null)
        {
            _androidChannelId = androidChannelId;
            _androidChannelName = androidChannelName;
            _androidChannelDescription = androidChannelDescription;
            _spacing = new(minimumSpacing);
        }

        public async UniTask<bool> RequestAuthorizationAsync(CancellationToken cancellationToken = default)
        {
            EnsureInitialized();

            var request = NotificationCenter.RequestPermission();
            await UniTask.WaitUntil(() => !request.keepWaiting, cancellationToken: cancellationToken);
            return request.Status == NotificationsPermissionStatus.Granted;
        }

        public void ScheduleLocal(LocalNotification notification)
        {
            if (notification == null)
                throw new ArgumentNullException(nameof(notification));

            EnsureInitialized();

            var fireAt = _spacing.Reserve(notification.FireAtUtc);
            var platformNotification = new Notification
            {
                Identifier = StableId(notification.Id),
                Title = notification.Title,
                Text = notification.Body
            };

            NotificationCenter.ScheduleNotification(
                platformNotification,
                new NotificationDateTimeSchedule(fireAt.ToLocalTime()));
        }

        public void CancelAll()
        {
            EnsureInitialized();

            _spacing.Clear();
            NotificationCenter.CancelAllScheduledNotifications();
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            var args = NotificationCenterArgs.Default;
            args.AndroidChannelId = _androidChannelId;
            args.AndroidChannelName = _androidChannelName;
            args.AndroidChannelDescription = _androidChannelDescription;
            NotificationCenter.Initialize(args);
        }

        // The platform API wants an int identifier; ours is a string like every other Foundation
        // id. A numeric id is used as-is, anything else gets a stable FNV-1a hash: string.GetHashCode
        // differs between runs, which would break cancelling or replacing a notification scheduled earlier.
        private static int StableId(string id)
        {
            if (int.TryParse(id, out var parsed))
                return parsed;
            if (string.IsNullOrEmpty(id))
                return 0;

            unchecked
            {
                var hash = 2166136261u;
                foreach (var character in id)
                    hash = (hash ^ character) * 16777619u;
                return (int)hash;
            }
        }
    }
}
