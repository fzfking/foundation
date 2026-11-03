using System;
using System.Threading;
using Foundation.Motion;
using UnityEngine;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// The scale a button's state gives it, shared by every scale strategy of that button so the newest state replaces
    /// the previous one (and its running tween) whichever asset asked. The first state a button shows, and any state
    /// shown while it is inactive, is applied at once; later changes tween.
    /// </summary>
    internal sealed class ButtonStateScale : IDisposable
    {
        private readonly ScaleContribution _contribution;
        private IMotionHandle _running;
        private bool _applied;

        public ButtonStateScale(Transform target)
        {
            _contribution = new ScaleContribution(target);
        }

        public static ButtonStateScale For(FoundationButtonStrategyContext context)
            => context.GetRuntime(typeof(ButtonStateScale), context.Button.transform, static target => new ButtonStateScale(target));

        public void Apply(FoundationButtonStrategyContext context, Vector3 scale, float duration, MotionEase ease, CancellationToken lifetime)
        {
            Stop();
            var animate = _applied && duration > 0f && context.ScopeBound && context.Button.isActiveAndEnabled;
            _applied = true;
            if (scale == _contribution.Factor)
                return;

            if (!animate)
            {
                _contribution.Set(scale);
                return;
            }

            _running = context.RequireMotion().Animate(
                _contribution.Factor,
                scale,
                duration,
                ease,
                _contribution,
                static (value, contribution) => contribution.Set(value),
                lifetime: lifetime);
        }

        public void Dispose() => Stop();

        private void Stop()
        {
            _running?.Cancel();
            _running = null;
        }
    }
}
