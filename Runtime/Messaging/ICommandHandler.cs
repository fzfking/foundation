using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface ICommandHandler<in TCommand>
    {
        UniTask HandleAsync(TCommand command, CancellationToken cancellationToken = default);
    }
}
