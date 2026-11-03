using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Motion
{
    /// <summary>How a motion repeats: <see cref="Count"/> passes, each a full duration; a yoyo reverses every other pass.</summary>
    public readonly struct MotionLoop
    {
        public int Count { get; }

        public bool Yoyo { get; }

        public static MotionLoop Once => new(1, false);

        /// <summary>Forward and back: ends where it started.</summary>
        public static MotionLoop PunchOut => new(2, true);

        public MotionLoop(int count, bool yoyo)
        {
            Count = Mathf.Max(1, count);
            Yoyo = yoyo;
        }
    }

    /// <summary>A running motion. It ends by completing, by <see cref="Cancel"/>, or when its lifetime token fires.</summary>
    public interface IMotionHandle
    {
        bool IsActive { get; }

        /// <summary>Stops the motion where it is; the value is left as the last update set it.</summary>
        void Cancel();

        /// <summary>
        /// Completes with the motion. Throws <see cref="OperationCanceledException"/> when the motion was
        /// cancelled; cancelling <paramref name="cancellationToken"/> cancels the motion.
        /// </summary>
        UniTask WaitAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Plays value tweens for UI and feedback animations. The built-in <see cref="PlayerLoopMotionService"/>
    /// needs only UniTask; an adapter (<c>Foundation.Infrastructure.LitMotion</c>) swaps in a tween library.
    /// </summary>
    /// <remarks>
    /// Every tween takes the object it updates plus a <c>static</c> callback, so starting one allocates no closure.
    /// <see cref="MotionServiceExtensions"/> adds the plain <c>Action</c> overloads for code that does not care.
    /// </remarks>
    public interface IMotionService
    {
        /// <summary>
        /// Tweens from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/>
        /// seconds (game time), calling <paramref name="onUpdate"/> with the eased value and <paramref name="state"/>
        /// each frame; a completed motion ends exactly on its last value. <paramref name="lifetime"/> cancels it
        /// (a destroyed owner).
        /// </summary>
        IMotionHandle Animate<TState>(
            float from,
            float to,
            float duration,
            MotionEase ease,
            TState state,
            Action<float, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class;

        /// <summary>
        /// The same tween for a vector. An engine that interpolates vectors itself overrides this; the default
        /// runs the float tween and interpolates here, which costs one small object per motion.
        /// </summary>
        IMotionHandle Animate<TState>(
            Vector2 from,
            Vector2 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector2, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Animate(0f, 1f, duration, ease, new MotionInterpolation<Vector2, TState>(from, to, state, onUpdate, MotionInterpolators.Vector2), MotionInterpolation<Vector2, TState>.Apply, loop, lifetime);

        /// <inheritdoc cref="Animate{TState}(Vector2,Vector2,float,MotionEase,TState,Action{Vector2,TState},MotionLoop,CancellationToken)"/>
        IMotionHandle Animate<TState>(
            Vector3 from,
            Vector3 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector3, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Animate(0f, 1f, duration, ease, new MotionInterpolation<Vector3, TState>(from, to, state, onUpdate, MotionInterpolators.Vector3), MotionInterpolation<Vector3, TState>.Apply, loop, lifetime);

        /// <inheritdoc cref="Animate{TState}(Vector2,Vector2,float,MotionEase,TState,Action{Vector2,TState},MotionLoop,CancellationToken)"/>
        IMotionHandle Animate<TState>(
            Quaternion from,
            Quaternion to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Quaternion, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Animate(0f, 1f, duration, ease, new MotionInterpolation<Quaternion, TState>(from, to, state, onUpdate, MotionInterpolators.Quaternion), MotionInterpolation<Quaternion, TState>.Apply, loop, lifetime);

        /// <inheritdoc cref="Animate{TState}(Vector2,Vector2,float,MotionEase,TState,Action{Vector2,TState},MotionLoop,CancellationToken)"/>
        IMotionHandle Animate<TState>(
            Color from,
            Color to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Color, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Animate(0f, 1f, duration, ease, new MotionInterpolation<Color, TState>(from, to, state, onUpdate, MotionInterpolators.Color), MotionInterpolation<Color, TState>.Apply, loop, lifetime);
    }
}
