using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface IQueryHandler<in TQuery, TResult>
    {
        UniTask<TResult> HandleAsync(TQuery query, CancellationToken cancellationToken = default);
    }
}
