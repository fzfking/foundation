using System.Threading;
using UnityEngine;

namespace Foundation.Feedbacks
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class FeedbackTargetLifetime : MonoBehaviour
    {
        private CancellationTokenSource _disabled;

        public CancellationToken DisabledToken => _disabled?.Token ?? new CancellationToken(true);

        private void OnEnable() => _disabled = new CancellationTokenSource();

        private void OnDisable()
        {
            _disabled?.Cancel();
            _disabled?.Dispose();
            _disabled = null;
        }
    }
}
