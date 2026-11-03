using UnityEngine;

namespace Foundation.Tutorial.Flow
{
    /// <summary>
    /// Anchors a <see cref="TutorialTargetRegistry"/> on a UI root. Targets below it register on enable, so
    /// views spawned without injection (list rows, popups) are still visible to scripts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialTargetHost : MonoBehaviour
    {
        private TutorialTargetRegistry _registry;

        public TutorialTargetRegistry Registry => _registry ??= new TutorialTargetRegistry();

        public static TutorialTargetRegistry FindRegistry(Transform transform)
        {
            var host = transform != null ? transform.GetComponentInParent<TutorialTargetHost>(true) : null;
            return host != null ? host.Registry : null;
        }
    }
}
