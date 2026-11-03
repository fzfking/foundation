using System;

namespace Foundation.GameFramework.Notifications
{
    public sealed class LocalNotification
    {
        public string Id;
        public string Title;
        public string Body;
        public DateTime FireAtUtc;
    }
}
