using System;

namespace Foundation.GameFramework.Notifications
{
    public interface INotificationSettings
    {
        bool Enabled { get; }

        event Action Changed;

        void SetEnabled(bool enabled);
    }
}
