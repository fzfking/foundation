using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface ICommandBus
    {
        UniTask SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default);
    }
}
