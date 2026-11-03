using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UIElements;

namespace Foundation.UI.Toolkit
{
    public interface IUiToolkitRoot
    {
        VisualElement Resolve(UILayer layer);
    }

    /// <summary>Full-screen views go to one container, every other layer to another.</summary>
    public sealed class UiToolkitRoot : IUiToolkitRoot
    {
        private readonly VisualElement _screens;
        private readonly VisualElement _popups;

        public UiToolkitRoot(VisualElement screens, VisualElement popups)
        {
            _screens = screens ?? throw new ArgumentNullException(nameof(screens));
            _popups = popups ?? throw new ArgumentNullException(nameof(popups));
        }

        public VisualElement Resolve(UILayer layer) => layer == UILayer.FullScreen ? _screens : _popups;
    }

    /// <summary>
    /// The UI Toolkit backend of the router: a view is an element tree under the container of its layer,
    /// hidden until shown. Definitions carry a <see cref="UiToolkitTemplate"/> as their source.
    /// </summary>
    public sealed class UiToolkitViewFactory : IUIViewFactory
    {
        private readonly IUiToolkitRoot _root;

        public UiToolkitViewFactory(IUiToolkitRoot root)
        {
            _root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.Source is not UiToolkitTemplate template)
                throw new InvalidOperationException(
                    $"UI definition '{definition.ViewId}' needs a {nameof(UiToolkitTemplate)} as its source.");

            var element = template.BuildTree();
            element.style.display = DisplayStyle.None;
            var view = template.CreateView();
            view.Bind(element);
            _root.Resolve(definition.Layer).Add(element);

            return UniTask.FromResult<IUIViewInstance>(new UIViewInstance(view, () => element.RemoveFromHierarchy()));
        }
    }
}
