using System;
using System.Threading;
using Foundation.Motion;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// The tint a button's state gives its graphics, shared by every color strategy of that button so the newest state
    /// replaces the previous one (and its running tween). The first state a button shows, and any state shown while it
    /// is inactive, is applied at once; later changes tween from each graphic's current color.
    /// </summary>
    internal sealed class ButtonStateColor : IDisposable
    {
        private readonly Graphic[] _graphics;
        private readonly Color[] _from;
        private Color _to;
        private IMotionHandle _running;
        private bool _applied;

        public ButtonStateColor(Graphic[] graphics)
        {
            _graphics = graphics;
            _from = new Color[graphics.Length];
        }

        public static ButtonStateColor For(FoundationButtonStrategyContext context)
            => context.GetRuntime(typeof(ButtonStateColor), context.Graphics, static graphics => new ButtonStateColor(graphics));

        public void Apply(FoundationButtonStrategyContext context, Color color, float duration, MotionEase ease, CancellationToken lifetime)
        {
            Stop();
            var animate = _applied && duration > 0f && context.ScopeBound && context.Button.isActiveAndEnabled;
            _applied = true;
            _to = color;
            if (!animate)
            {
                Blend(1f);
                return;
            }

            var differs = false;
            for (var index = 0; index < _graphics.Length; index++)
            {
                if (_graphics[index] == null)
                    continue;

                _from[index] = _graphics[index].color;
                differs |= _from[index] != color;
            }

            if (!differs)
                return;

            _running = context.RequireMotion().Animate(
                0f,
                1f,
                duration,
                ease,
                this,
                static (progress, state) => state.Blend(progress),
                lifetime: lifetime);
        }

        public void Dispose() => Stop();

        private void Blend(float progress)
        {
            for (var index = 0; index < _graphics.Length; index++)
            {
                if (_graphics[index] == null)
                    continue;

                _graphics[index].color = progress >= 1f ? _to : Color.Lerp(_from[index], _to, progress);
            }
        }

        private void Stop()
        {
            _running?.Cancel();
            _running = null;
        }
    }
}
