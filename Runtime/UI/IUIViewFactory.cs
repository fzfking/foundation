using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    /// <summary>
    /// The UI backend as the router sees it. Everything that depends on how views are drawn (parenting,
    /// creation from a template or asset, injection, hiding until shown) lives behind this, so the router
    /// (stack, cache, arguments, Back) is the same for uGUI, UI Toolkit, IMGUI or anything else.
    /// </summary>
    public interface IUIViewFactory
    {
        /// <summary>
        /// Creates the view of <paramref name="definition"/> in its layer, built from the definition's
        /// template or asset, given its services, and hidden; the router shows it. Throws when the definition
        /// yields no <see cref="IUIView"/>.
        /// </summary>
        UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken);
    }

    /// <summary>A created view and what it holds: disposing it destroys the view and releases its asset.</summary>
    public interface IUIViewInstance : IDisposable
    {
        IUIView View { get; }
    }

    public sealed class UIViewInstance : IUIViewInstance
    {
        private Action _dispose;

        public IUIView View { get; }

        public UIViewInstance(IUIView view, Action dispose = null)
        {
            View = view ?? throw new ArgumentNullException(nameof(view));
            _dispose = dispose;
        }

        public void Dispose()
        {
            var dispose = _dispose;
            _dispose = null;
            dispose?.Invoke();
        }
    }
}
