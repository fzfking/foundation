using System.Threading;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// Tints every graphic of the button when it enters a state. The change tweens over <see cref="duration"/>; the
    /// first state a button shows is applied at once. It does not wait for the tween, so the other strategies of the
    /// state start together with it.
    /// </summary>
    [CreateAssetMenu(menuName = "Foundation/UI/Button State Strategies/Color")]
    public sealed class FoundationButtonStateColorStrategy : FoundationButtonStateStrategy
    {
        [SerializeField] private Color color = Color.white;

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

            ButtonStateColor.For(context).Apply(context, color, duration, ease, cancellationToken);
            return UniTask.CompletedTask;
        }
    }
}
