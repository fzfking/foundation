using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface IQueryBus
    {
        UniTask<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default);
    }
}
