using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.SystemPermissions
{
    public enum PermissionState
    {
        Granted,
        Denied,
        NotDetermined
    }

    public interface IPermissionService
    {
        UniTask<PermissionState> CheckAsync(string permissionId, CancellationToken cancellationToken = default);
        UniTask<PermissionState> RequestAsync(string permissionId, CancellationToken cancellationToken = default);
    }
}
