using System;
using System.Threading;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using LitMotion;
using UnityEngine;

namespace Foundation.Infrastructure.LitMotion
{
    /// <summary>
    /// <see cref="IMotionService"/> on LitMotion: tweens run by LitMotion's scheduler instead of the built-in
    /// player-loop loops. Starting one allocates the returned handle (and one delegate when a lifetime is given); the
    /// caller's state and callback are passed straight through, so no closure is created.
    /// </summary>
    public sealed class LitMotionService : IMotionService
    {
        // The Foundation's curves are matched to LitMotion's by name; the two enums list them in different orders.
        private static readonly Ease[] Eases = BuildEaseMap();

        public IMotionHandle Animate<TState>(
            float from,
            float to,
            float duration,
            MotionEase ease,
            TState state,
            Action<float, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Start(LMotion.Create(from, to, duration), ease, state, onUpdate, loop, lifetime);

        // LitMotion interpolates vectors, rotations and colours itself, so these skip the float tween the default builds.
        public IMotionHandle Animate<TState>(
            Vector2 from,
            Vector2 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector2, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Start(LMotion.Create(from, to, duration), ease, state, onUpdate, loop, lifetime);

        public IMotionHandle Animate<TState>(
            Vector3 from,
            Vector3 to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Vector3, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Start(LMotion.Create(from, to, duration), ease, state, onUpdate, loop, lifetime);

        public IMotionHandle Animate<TState>(
            Quaternion from,
            Quaternion to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Quaternion, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Start(LMotion.Create(from, to, duration), ease, state, onUpdate, loop, lifetime);

        public IMotionHandle Animate<TState>(
            Color from,
            Color to,
            float duration,
            MotionEase ease,
            TState state,
            Action<Color, TState> onUpdate,
            MotionLoop loop = default,
            CancellationToken lifetime = default)
            where TState : class
            => Start(LMotion.Create(from, to, duration), ease, state, onUpdate, loop, lifetime);

        private static IMotionHandle Start<TValue, TOptions, TAdapter, TState>(
            MotionBuilder<TValue, TOptions, TAdapter> builder,
            MotionEase ease,
            TState state,
            Action<TValue, TState> onUpdate,
            MotionLoop loop,
            CancellationToken lifetime)
            where TValue : unmanaged
            where TOptions : unmanaged, IMotionOptions
            where TAdapter : unmanaged, IMotionAdapter<TValue, TOptions>
            where TState : class
        {
            if (onUpdate == null)
                throw new ArgumentNullException(nameof(onUpdate));

            var passes = Math.Max(1, loop.Count);
            builder = builder.WithEase(Eases[(int)ease]);
            if (passes > 1)
                builder = builder.WithLoops(passes, loop.Yoyo ? LoopType.Yoyo : LoopType.Restart);

            // The lifetime registration is dropped when the motion ends, so frequent short motions on a long-lived owner do
            // not pile callbacks onto its token.
            var handle = new Handle();
            if (lifetime.CanBeCanceled)
            {
                Action release = handle.Release;
                builder = builder.WithOnComplete(release).WithOnCancel(release);
            }

            handle.Attach(builder.Bind(state, onUpdate));
            if (lifetime.CanBeCanceled)
                handle.Follow(lifetime);

            return handle;
        }

        private static Ease[] BuildEaseMap()
        {
            // Indexed by the enum value, not by position in the name list: the map must not shift if a value is ever skipped.
            var values = (MotionEase[])Enum.GetValues(typeof(MotionEase));
            var map = new Ease[(int)values[^1] + 1];
            foreach (var value in values)
                map[(int)value] = Enum.TryParse(value.ToString(), out Ease ease) ? ease : Ease.Linear;

            return map;
        }

        private sealed class Handle : IMotionHandle
        {
            private MotionHandle _handle;
            private CancellationTokenRegistration _registration;

            public bool IsActive => _handle.IsActive();

            public void Attach(MotionHandle handle) => _handle = handle;

            // The token calls the cached static callback with this handle, so following a lifetime allocates no closure.
            public void Follow(CancellationToken lifetime) =>
                _registration = lifetime.Register(static handle => ((Handle)handle).Cancel(), this);

            public void Release() => _registration.Dispose();

            public void Cancel()
            {
                if (_handle.IsActive())
                    _handle.Cancel();
            }

            public UniTask WaitAsync(CancellationToken cancellationToken = default) => _handle.ToUniTask(cancellationToken);
        }
    }
}
