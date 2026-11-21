using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Foundation.UI.Toolkit
{
    /// <summary>Centers a fixed-size element tree in its parent using a uniform scale.</summary>
    public sealed class UiToolkitDesignViewport : VisualElement
    {
        private readonly Vector2 _referenceResolution;

        public VisualElement Content { get; }

        public UiToolkitDesignViewport(Vector2 referenceResolution)
        {
            if (!(referenceResolution.x > 0f && referenceResolution.y > 0f) ||
                float.IsInfinity(referenceResolution.x) || float.IsInfinity(referenceResolution.y))
                throw new ArgumentOutOfRangeException(nameof(referenceResolution));

            _referenceResolution = referenceResolution;
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            Content = new VisualElement { pickingMode = PickingMode.Ignore };
            Content.style.position = Position.Absolute;
            Content.style.width = referenceResolution.x;
            Content.style.height = referenceResolution.y;
            Content.style.transformOrigin = new TransformOrigin(0f, 0f);
            Add(Content);
            RegisterCallback<GeometryChangedEvent>(_ => Fit());
        }

        private void Fit()
        {
            var bounds = contentRect;
            if (!(bounds.width > 0f && bounds.height > 0f))
                return;

            var scale = Mathf.Min(bounds.width / _referenceResolution.x, bounds.height / _referenceResolution.y);
            var size = _referenceResolution * scale;
            Content.style.left = bounds.x + (bounds.width - size.x) * 0.5f;
            Content.style.top = bounds.y + (bounds.height - size.y) * 0.5f;
            Content.style.scale = new Scale(Vector2.one * scale);
        }
    }
}
