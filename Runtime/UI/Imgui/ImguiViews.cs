using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.Imgui
{
    /// <summary>A view drawn with IMGUI calls; the host calls <see cref="Draw"/> from <c>OnGUI</c> while it is shown.</summary>
    public interface IImguiView : IUIView
    {
        bool IsShown { get; }
        void Draw(Rect screen);
    }

    /// <summary>A view that tracks its shown state; override <see cref="Draw"/> and call <c>GUI</c> / <c>GUILayout</c>.</summary>
    public abstract class ImguiView : IImguiView
    {
        public bool IsShown { get; private set; }

        public virtual UniTask ShowAsync(CancellationToken cancellationToken = default)
        {
            IsShown = true;
            return UniTask.CompletedTask;
        }

        public virtual UniTask HideAsync(CancellationToken cancellationToken = default)
        {
            IsShown = false;
            return UniTask.CompletedTask;
        }

        public abstract void Draw(Rect screen);
    }

    /// <summary>The <see cref="UiDefinition"/> source of this backend: how to create the view.</summary>
    public sealed class ImguiTemplate : IUIViewSource
    {
        private readonly Func<IImguiView> _create;
        public ImguiTemplate(Func<IImguiView> create) => _create = create ?? throw new ArgumentNullException(nameof(create));
        internal IImguiView Create() => _create();
    }

    /// <summary>
    /// The scene component that owns IMGUI drawing: draws shown views, lowest layer first, in creation order.
    /// Put one in the scene and give it to <see cref="ImguiViewFactory"/>.
    /// </summary>
    public sealed class ImguiHost : MonoBehaviour
    {
        private readonly List<(UILayer Layer, IImguiView View)> _views = new List<(UILayer, IImguiView)>();

        public int ViewCount => _views.Count;

        private void OnGUI() => DrawAll(new Rect(0f, 0f, Screen.width, Screen.height));

        public void Add(UILayer layer, IImguiView view)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));

            _views.Add((layer, view));
        }

        public void Remove(IImguiView view) => _views.RemoveAll(entry => ReferenceEquals(entry.View, view));

        public void DrawAll(Rect screen)
        {
            for (var layer = UILayer.FullScreen; layer <= UILayer.Popup; layer++)
            {
                foreach (var entry in _views.ToArray())
                {
                    if (entry.Layer == layer && entry.View.IsShown)
                        entry.View.Draw(screen);
                }
            }
        }
    }

    public sealed class ImguiViewFactory : IUIViewFactory
    {
        private readonly ImguiHost _host;

        public ImguiViewFactory(ImguiHost host) => _host = host != null ? host : throw new ArgumentNullException(nameof(host));

        public UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (definition.Source is not ImguiTemplate template)
                throw new InvalidOperationException(
                    $"UI definition '{definition.ViewId}' needs an {nameof(ImguiTemplate)} as its source.");

            var view = template.Create();
            _host.Add(definition.Layer, view);
            return UniTask.FromResult<IUIViewInstance>(new UIViewInstance(view, () => _host.Remove(view)));
        }
    }
}
