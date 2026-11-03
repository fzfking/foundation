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
    /// callbacks; override <see cref="ShowAsync"/> / <see cref="HideAsync"/> to animate (USS transitions, a
    /// tween).
    /// </summary>
    public abstract class UiToolkitView : IUiToolkitView
    {
        protected VisualElement Root { get; private set; }

        public void Bind(VisualElement root)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            OnBound(root);
        }

        public virtual UniTask ShowAsync(CancellationToken cancellationToken = default)
        {
            Root.style.display = DisplayStyle.Flex;
            return UniTask.CompletedTask;
        }

        public virtual UniTask HideAsync(CancellationToken cancellationToken = default)
        {
            Root.style.display = DisplayStyle.None;
            return UniTask.CompletedTask;
        }

        protected virtual void OnBound(VisualElement root)
        {
        }
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

        internal VisualElement BuildTree() => _buildTree();

        internal IUiToolkitView CreateView() => _createView();
    }
}
