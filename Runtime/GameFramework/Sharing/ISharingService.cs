using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Sharing
{
    public sealed class SharePayload
    {
        public string Text;
        public string Url;
    }

    public interface ISharingService
    {
        UniTask ShareAsync(SharePayload payload, CancellationToken cancellationToken = default);
    }
}
