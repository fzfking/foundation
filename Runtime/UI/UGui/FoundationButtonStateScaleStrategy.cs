using System.Threading;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// Scales the button when it enters a state, as a multiple of the size the button has otherwise (so it composes
    /// with a press punch instead of fighting it). The change tweens over <see cref="duration"/>; the first state
    /// a button shows is applied at once. It does not wait for the tween, so the other strategies of the state start
    /// together with it.
    /// </summary>
    [CreateAssetMenu(menuName = "Foundation/UI/Button State Strategies/Scale")]
    public sealed class FoundationButtonStateScaleStrategy : FoundationButtonStateStrategy
    {
        [SerializeField] private Vector3 scale = Vector3.one;

        [SerializeField, Min(0f), Tooltip("Seconds the change takes; 0 applies it at once.")]
        private float duration = 0.12f;

        [SerializeField] private MotionEase ease = MotionEase.OutCubic;

        public override UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken)
        {
            if (!context.IsCurrentState(stateId))
                return UniTask.CompletedTask;

            ButtonStateScale.For(context).Apply(context, scale, duration, ease, cancellationToken);
            return UniTask.CompletedTask;
        }

        private void OnValidate()
        {
            scale = new Vector3(
                Mathf.Max(ScaleContribution.MinimumFactor, scale.x),
                Mathf.Max(ScaleContribution.MinimumFactor, scale.y),
                Mathf.Max(ScaleContribution.MinimumFactor, scale.z));
        }
    }
}
