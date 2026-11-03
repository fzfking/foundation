using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface IEventHandler<in TEvent>
    {
        UniTask HandleAsync(TEvent message, CancellationToken cancellationToken = default);
    }
}
