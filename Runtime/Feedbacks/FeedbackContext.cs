using UnityEngine;

namespace Foundation.Feedbacks
{
    public readonly struct FeedbackContext
    {
        public GameObject Target { get; }
        public Vector3 Position { get; }

        public bool HasTarget => Target != null;

        public FeedbackContext(GameObject target, Vector3 position = default)
        {
            Target = target;
            Position = position;
        }

        public static FeedbackContext For(Component target) => new(target != null ? target.gameObject : null);

        public static FeedbackContext For(GameObject target) => new(target);

        public static FeedbackContext At(Vector3 position) => new(null, position);
    }
}
