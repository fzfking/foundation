using System;
using UnityEngine;

namespace Foundation.UI.UGui
{
    public interface IUIRoot
    {
        Transform Resolve(UILayer layer);
    }

    public sealed class TransformUIRoot : IUIRoot
    {
        private readonly Transform _screenRoot;
        private readonly Transform _popupRoot;

        public TransformUIRoot(Transform screenRoot, Transform popupRoot)
        {
            _screenRoot = screenRoot != null ? screenRoot : throw new ArgumentNullException(nameof(screenRoot));
            _popupRoot = popupRoot != null ? popupRoot : throw new ArgumentNullException(nameof(popupRoot));
        }

        public Transform Resolve(UILayer layer) => layer == UILayer.FullScreen ? _screenRoot : _popupRoot;
    }

    public sealed class LayerUIRoot : IUIRoot
    {
        private readonly Transform _root;
        private readonly UILayer _layer;

        public LayerUIRoot(Transform root, UILayer layer)
        {
            _root = root != null ? root : throw new ArgumentNullException(nameof(root));
            _layer = layer;
        }

        public Transform Resolve(UILayer layer)
        {
            if (layer != _layer)
                throw new InvalidOperationException($"UI layer '{layer}' is not available in this scope. Expected '{_layer}'.");

            return _root;
        }
    }
}
