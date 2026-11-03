using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Foundation.Tutorial.Flow
{
    /// <summary>
    /// Marks a UI element as a tutorial target. Set the id in the prefab, or at runtime with
    /// <see cref="Attach"/> when it depends on data (e.g. <c>production.night-hunt.buy</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialTarget : MonoBehaviour, ITutorialTarget, IPointerClickHandler
    {
        [SerializeField] private string targetId;

        private TutorialTargetRegistry _registry;
        private string _registeredId;

        public event Action Clicked;

        public string Id => targetId;

        public RectTransform Rect => transform as RectTransform;

        public bool IsAvailable => this != null && isActiveAndEnabled && gameObject.activeInHierarchy;

        private void OnEnable() => Register();

        private void OnDisable() => Unregister();

        public static TutorialTarget Attach(GameObject gameObject, string id)
        {
            if (gameObject == null)
                return null;

            if (!gameObject.TryGetComponent(out TutorialTarget target))
                target = gameObject.AddComponent<TutorialTarget>();
            target.SetId(id);
            return target;
        }

        public void SetId(string id)
        {
            if (string.Equals(targetId, id, StringComparison.Ordinal))
                return;

            Unregister();
            targetId = id;
            if (isActiveAndEnabled)
                Register();
        }

        /// <summary>Lets code that owns a custom input path report a click on this target.</summary>
        public void NotifyClicked() => Clicked?.Invoke();

        public void OnPointerClick(PointerEventData eventData) => NotifyClicked();

        private void Register()
        {
            if (string.IsNullOrEmpty(targetId))
                return;

            _registry ??= TutorialTargetHost.FindRegistry(transform);
            if (_registry == null)
                return;

            _registry.Register(this);
            _registeredId = targetId;
        }

        private void Unregister()
        {
            if (_registry == null || _registeredId == null)
                return;

            _registry.Unregister(this, _registeredId);
            _registeredId = null;
        }
    }
}
