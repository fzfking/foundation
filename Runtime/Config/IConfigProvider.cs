using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Config
{
    public interface IConfigProvider
    {
        UniTask EnsureAsync<TConfig>(CancellationToken cancellationToken = default);
    }
}
