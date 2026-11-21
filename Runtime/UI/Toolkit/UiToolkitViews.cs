using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Foundation.UI.Toolkit
{
    /// <summary>A view backed by a UI Toolkit element tree. The factory builds the tree and hands it over once.</summary>
    public interface IUiToolkitView : IUIView
    {
        void Bind(VisualElement root);
    }

    /// <summary>
    /// A view that shows and hides its element. Override <see cref="OnBound"/> to query the tree and wire
    /// callbacks. Work started on show can use <see cref="VisibilityToken"/> until the next hide.
    /// </summary>
    public abstract class UiToolkitView : IUiToolkitView, IDisposable
    {
        private CancellationTokenSource _visibility;
        private bool _disposed;

        public bool IsShown => _visibility != null;

        protected VisualElement Root { get; private set; }
        protected CancellationToken VisibilityToken => _visibility?.Token ?? new CancellationToken(true);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                Hide();
            }
            finally
            {
                try
                {
                    OnDispose();
                }
                finally
                {
                    Root = null;
                }
            }
        }

        public void Bind(VisualElement root)
        {
            ThrowIfDisposed();
            if (Root != null)
                throw new InvalidOperationException("A UI Toolkit view can only be bound once.");

            Root = root ?? throw new ArgumentNullException(nameof(root));
            OnBound(root);
        }

        public virtual UniTask HideAsync(CancellationToken cancellationToken = default)
        {
            Hide();
            return UniTask.CompletedTask;
        }

        protected virtual void OnBound(VisualElement root)
        {
        }

        protected virtual void OnHide()
        {
        }

        /// <summary>Releases resources acquired by the view, including resources acquired during a failed bind.</summary>
        protected virtual void OnDispose()
        {
        }

        private void Hide()
        {
            var visibility = _visibility;
            if (visibility == null)
                return;

            _visibility = null;
            try
            {
                Root.style.display = DisplayStyle.None;
            }
            catch (Exception exception) when (exception is NullReferenceException or InvalidOperationException)
            {
                // PanelRenderer can release style data before disposing the view.
            }
            finally
            {
                try
                {
                    visibility.Cancel();
                }
                finally
                {
                    visibility.Dispose();
                    OnHide();
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public virtual async UniTask ShowAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();
            if (IsShown)
                return;
            if (Root == null)
                throw new InvalidOperationException("Bind the view before showing it.");

            var visibility = new CancellationTokenSource();
            var token = visibility.Token;
            _visibility = visibility;
            try
            {
                // An open request owns cancellation only until the show hook completes.
                using var showCancellation = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
                showCancellation.Token.ThrowIfCancellationRequested();
                Root.style.display = DisplayStyle.Flex;
                await OnShowAsync(showCancellation.Token);
                showCancellation.Token.ThrowIfCancellationRequested();
            }
            catch
            {
                if (ReferenceEquals(_visibility, visibility))
                    Hide();
                throw;
            }
        }

        protected virtual UniTask OnShowAsync(CancellationToken cancellationToken) => UniTask.CompletedTask;
    }

    /// <summary>
    /// What a <see cref="UiDefinition"/> holds as its template for this backend: how to build the element
    /// tree (usually <c>VisualTreeAsset.Instantiate</c>) and the view that drives it.
    /// </summary>
    public sealed class UiToolkitTemplate : IUIViewSource
    {
        private readonly Func<VisualElement> _buildTree;
        private readonly Func<IUiToolkitView> _createView;

        public UiToolkitTemplate(Func<VisualElement> buildTree, Func<IUiToolkitView> createView)
        {
            _buildTree = buildTree ?? throw new ArgumentNullException(nameof(buildTree));
            _createView = createView ?? throw new ArgumentNullException(nameof(createView));
        }

        public UiToolkitTemplate(VisualTreeAsset tree, Func<IUiToolkitView> createView)
            : this(tree != null ? (Func<VisualElement>)(() => tree.Instantiate()) : throw new ArgumentNullException(nameof(tree)), createView)
        {
        }

        internal VisualElement BuildTree() =>
            _buildTree() ?? throw new InvalidOperationException("The UI Toolkit template returned no element tree.");

        internal IUiToolkitView CreateView() =>
            _createView() ?? throw new InvalidOperationException("The UI Toolkit template returned no view.");
    }
}
