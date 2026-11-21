#if FOUNDATION_UITOOLKIT
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Foundation.Tutorial.Flow
{
    /// <summary>Registers an element while it belongs to a panel. The view owns and disposes this adapter.</summary>
    public sealed class UiToolkitTutorialTarget : ITutorialTarget, IDisposable
    {
        private readonly TutorialTargetRegistry _registry;
        private readonly bool _observeClickEvents;

        private bool _attached;
        private bool _disposed;

        public event Action Clicked;

        public string Id { get; private set; }
        public VisualElement Element { get; }
        public RectTransform Rect => null;

        public bool IsAvailable
        {
            get
            {
                if (!_attached || !Element.visible || !Element.enabledInHierarchy)
                    return false;

                for (var element = Element; element != null; element = element.hierarchy.parent)
                {
                    if (element.resolvedStyle.display == DisplayStyle.None)
                        return false;
                }

                return true;
            }
        }

        public UiToolkitTutorialTarget(TutorialTargetRegistry registry, string id, VisualElement element)
            : this(registry, id, element, observeClickEvents: true)
        {
        }

        public UiToolkitTutorialTarget(TutorialTargetRegistry registry, string id, VisualElement element, bool observeClickEvents)
        {
            _observeClickEvents = observeClickEvents;
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Element = element ?? throw new ArgumentNullException(nameof(element));
            Id = id;

            Element.RegisterCallback<AttachToPanelEvent>(OnAttach);
            Element.RegisterCallback<DetachFromPanelEvent>(OnDetach);
            if (_observeClickEvents)
                Element.RegisterCallback<ClickEvent>(OnClick);
            if (Element.panel != null)
                Register();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Unregister();
            Element.UnregisterCallback<AttachToPanelEvent>(OnAttach);
            Element.UnregisterCallback<DetachFromPanelEvent>(OnDetach);
            if (_observeClickEvents)
                Element.UnregisterCallback<ClickEvent>(OnClick);
            Clicked = null;
        }

        public void SetId(string id)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UiToolkitTutorialTarget));
            if (string.Equals(Id, id, StringComparison.Ordinal))
                return;

            _registry.Unregister(this, Id);
            Id = id;
            if (_attached)
                _registry.Register(this);
        }

        /// <summary>Reports a click from controls with their own input path.</summary>
        public void NotifyClicked()
        {
            if (IsAvailable)
                Clicked?.Invoke();
        }

        /// <summary>Gets the laid-out bounds in the element's panel coordinates.</summary>
        public bool TryGetWorldBounds(out Rect bounds)
        {
            bounds = default;
            if (!IsAvailable)
                return false;

            var rect = Element.worldBound;
            if (!(rect.width > 0f && rect.height > 0f && float.IsFinite(rect.xMax) && float.IsFinite(rect.yMax)))
                return false;

            bounds = rect;
            return true;
        }

        private void OnAttach(AttachToPanelEvent evt)
        {
            if (evt.target == Element)
                Register();
        }

        private void OnDetach(DetachFromPanelEvent evt)
        {
            if (evt.target == Element)
                Unregister();
        }

        private void OnClick(ClickEvent evt) => NotifyClicked();

        private void Register()
        {
            _attached = true;
            _registry.Register(this);
        }

        private void Unregister()
        {
            _attached = false;
            _registry.Unregister(this, Id);
        }
    }
}
#endif
