using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Scripting;

namespace Foundation.UI
{
    /// <summary>
    /// An open popup on the popup stack. Closing it runs the popup's hide and removes it. Disposing it closes it the
    /// same way without waiting, and closing an already closed popup does nothing.
    /// </summary>
    public sealed class PopupHandle<TView> : IDisposable
        where TView : class, IUIView
    {
        private readonly IUIRouter _router;
        private bool _closed;
        private bool _closing;

        public TView View { get; }

        internal PopupHandle(IUIRouter router, TView view)
        {
            _router = router;
            View = view;
        }

        public void Dispose() => CloseAsync().Forget();

        public async UniTask CloseAsync(CancellationToken cancellationToken = default)
        {
            if (_closed || _closing)
                return;

            _closing = true;
            try
            {
                await _router.CloseAsync(View, cancellationToken);
                _closed = true;
            }
            finally
            {
                _closing = false;
            }
        }
    }

    public static class PopupRouting
    {
        /// <summary>Opens a popup on the popup stack and returns the handle that closes it.</summary>
        /// <exception cref="InvalidOperationException">The definition does not produce a <typeparamref name="TView"/>.</exception>
        public static async UniTask<PopupHandle<TView>> OpenPopupAsync<TView>(
            this IUIRouter router,
            UiDefinition definition,
            UiArgs args = default,
            CancellationToken cancellationToken = default)
            where TView : class, IUIView
        {
            var view = await router.OpenAsync<TView>(definition, args, cancellationToken);
            return new PopupHandle<TView>(router, view);
        }
    }

    // Instantiates the generic popup API once with a reference type, so IL2CPP compiles its shared code in this AOT
    // assembly and hot-update (HybridCLR) code opening popups of its own view types does not hit a missing AOT
    // instantiation. Never called.
    [Preserve]
    internal static class PopupAotHints
    {
        [Preserve]
        private static void Instantiate(IUIRouter router)
        {
            router.OpenPopupAsync<IUIView>(null).Forget();
            Use(default(PopupHandle<IUIView>));
        }

        private static void Use<T>(T value)
        {
        }
    }
}
