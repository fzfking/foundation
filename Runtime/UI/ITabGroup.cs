using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    public interface ITabGroup
    {
        string SelectedTabId { get; }
        UniTask SelectAsync(string tabId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// A tab group that reports a selection it could not start instead of silently dropping it. Kept apart from
    /// <see cref="ITabGroup"/> so existing implementations of that contract stay valid.
    /// </summary>
    public interface ITabGroupTrySelect
    {
        /// <summary>
        /// Starts the selection unless another one is still transitioning; then returns false at once and does not
        /// queue the request. Returns true when the tab is selected, including when it already was.
        /// </summary>
        UniTask<bool> TrySelectAsync(string tabId, CancellationToken cancellationToken = default);
    }
}
