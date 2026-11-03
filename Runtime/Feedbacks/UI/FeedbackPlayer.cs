using Foundation.DI;
using UnityEngine;

namespace Foundation.Feedbacks.UI
{
    [DisallowMultipleComponent]
    public sealed class FeedbackPlayer : MonoBehaviour, IServiceResolverClient
    {
        [SerializeField] private FeedbackId feedbackId;
        [SerializeField] private GameObject target;

        private IFeedbackService _feedbacks;
        private FeedbackHandle _handle;

        private void OnDisable() => _handle?.Cancel();

        void IServiceResolverClient.Bind(IServiceResolver resolver) => Bind(resolver.Resolve<IFeedbackService>());

        public void Bind(IFeedbackService feedbacks) => _feedbacks = feedbacks;

        public void Play()
        {
            if (_feedbacks == null || feedbackId.IsEmpty)
                return;
            _handle = _feedbacks.Play(feedbackId, FeedbackContext.For(target != null ? target : gameObject));
        }
    }
}
