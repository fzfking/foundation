using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    /// <summary>
    /// A view that decides what Back does while it is on top of the stack instead of closing. Return true
    /// when handled. A view Back must not close returns true and does nothing; one whose dismissal is another
    /// button presses that button. The router calls this outside its operation gate, so the handler may open
    /// or close views. It cancels the token when the entry leaves the stack and keeps the view alive until
    /// the handler completes; handlers should observe the token.
    /// </summary>
    public interface IUIBackHandler
    {
        UniTask<bool> HandleBackAsync(CancellationToken cancellationToken = default);
    }
}
