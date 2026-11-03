using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    public interface IUIView
    {
        UniTask ShowAsync(CancellationToken cancellationToken = default);
        UniTask HideAsync(CancellationToken cancellationToken = default);
    }
}
