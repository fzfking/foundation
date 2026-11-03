using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Sharing
{
    public sealed class NullSharingService : ISharingService
    {
        public UniTask ShareAsync(SharePayload payload, CancellationToken cancellationToken = default)
            => UniTask.CompletedTask;
    }
}
