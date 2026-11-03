using System;
using System.Threading;
using UnityEngine;

namespace Foundation.Motion
{
    /// <summary>
    /// Tweens that take a plain <see cref="Action{T}"/>. The caller's lambda is the only allocation; pass a state object
    /// and a <c>static</c> callback to <see cref="IMotionService"/> directly to avoid it.
    /// </summary>
    public static class MotionServiceExtensions
    {
        // The interface's default vector tweens are invisible through a concrete service type (PlayerLoopMotionService
        // and the like); these forward to them. Called on an IMotionService, the interface member wins.
        public static IMotionHandle Animate<TState>(
            this IMotionService motion,
            Vector2 from,
            Vector2 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector2, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => motion.Animate(from, to, duration, ease, state, onUpdate, loop, lifetime);

        public static IMotionHandle Animate<TState>(
            this IMotionService motion,
            Vector3 from,
            Vector3 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector3, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => motion.Animate(from, to, duration, ease, state, onUpdate, loop, lifetime);

        public static IMotionHandle Animate<TState>(
            this IMotionService motion,
            Quaternion from,
            Quaternion to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Quaternion, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => motion.Animate(from, to, duration, ease, state, onUpdate, loop, lifetime);

        public static IMotionHandle Animate<TState>(
            this IMotionService motion,
            Color from,
            Color to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Color, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => motion.Animate(from, to, duration, ease, state, onUpdate, loop, lifetime);

        public static IMotionHandle Animate(
            this IMotionService motion,
            float from,
            float to,
            float duration,
            MotionEase ease,
            Action<float> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            => motion.Animate(from, to, duration, ease, onUpdate, static (value, callback) => callback(value), loop, lifetime);

        public static IMotionHandle Animate(
            this IMotionService motion,
            Vector2 from,
            Vector2 to,
            float duration,
            MotionEase ease,
            Action<Vector2> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            => motion.Animate(from, to, duration, ease, onUpdate, static (value, callback) => callback(value), loop, lifetime);

        public static IMotionHandle Animate(
            this IMotionService motion,
            Vector3 from,
            Vector3 to,
            float duration,
            MotionEase ease,
            Action<Vector3> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            => motion.Animate(from, to, duration, ease, onUpdate, static (value, callback) => callback(value), loop, lifetime);

        public static IMotionHandle Animate(
            this IMotionService motion,
            Quaternion from,
            Quaternion to,
            float duration,
            MotionEase ease,
            Action<Quaternion> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            => motion.Animate(from, to, duration, ease, onUpdate, static (value, callback) => callback(value), loop, lifetime);

        public static IMotionHandle Animate(
            this IMotionService motion,
            Color from,
            Color to,
            float duration,
            MotionEase ease,
            Action<Color> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            => motion.Animate(from, to, duration, ease, onUpdate, static (value, callback) => callback(value), loop, lifetime);
    }
}
