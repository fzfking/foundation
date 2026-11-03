using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Notifications
{
    public interface IPushNotificationService
    {
        UniTask<bool> RequestAuthorizationAsync(CancellationToken cancellationToken = default);
        void ScheduleLocal(LocalNotification notification);
        void CancelAll();
    }
}
