using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityTime = UnityEngine.Time;

namespace Foundation.Motion
{
    /// <summary>
    /// The built-in <see cref="IMotionService"/>: one small UniTask loop per running motion, stepped on the
    /// player loop's Update with game time. Enough for UI feedback; use a tween-library adapter when many
    /// motions run at once. It keeps no state, so a single instance can be registered for the whole scope.
    /// </summary>
    public sealed class PlayerLoopMotionService : IMotionService
    {
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
        {
            if (onUpdate == null)
                throw new ArgumentNullException(nameof(onUpdate));
            if (float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));

            var handle = new Handle(lifetime);
            handle.RunAsync(from, to, duration, ease, state, onUpdate, loop).Forget();
            return handle;
        }

        private sealed class Handle : IMotionHandle
        {
            private readonly CancellationTokenSource _cancellation;
            private readonly UniTaskCompletionSource _completion = new();

            public bool IsActive { get; private set; } = true;

            public Handle(CancellationToken lifetime)
            {
                _cancellation = lifetime.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(lifetime)
                    : new CancellationTokenSource();
            }

            public void Cancel()
            {
                if (IsActive)
                    _cancellation.Cancel();
            }

            public async UniTask WaitAsync(CancellationToken cancellationToken = default)
            {
                if (!cancellationToken.CanBeCanceled)
                {
                    await _completion.Task;
                    return;
                }

                using (cancellationToken.Register(Cancel))
                    await _completion.Task;
            }

            public async UniTaskVoid RunAsync<TState>(
                float from,
                float to,
                float duration,
                MotionEase ease,
                TState state,
                Action<float, TState> onUpdate,
                MotionLoop loop)
            {
                var token = _cancellation.Token;
                var passes = Mathf.Max(1, loop.Count);
                try
                {
                    // The first update waits for the next frame, so a motion started in a component's Awake never
                    // runs before the rest of the scene has been built.
                    await UniTask.Yield(PlayerLoopTiming.Update, token);

                    for (var pass = 0; pass < passes; pass++)
                    {
                        var reversed = loop.Yoyo && pass % 2 == 1;
                        var elapsed = 0f;
                        while (elapsed < duration)
                        {
                            var progress = MotionEasing.Evaluate(ease, elapsed / duration);
                            onUpdate(Mathf.LerpUnclamped(reversed ? to : from, reversed ? from : to, progress), state);
                            await UniTask.Yield(PlayerLoopTiming.Update, token);
                            elapsed += UnityTime.deltaTime;
                        }

                        onUpdate(reversed ? from : to, state);
                    }

                    Finish(null);
                }
                catch (OperationCanceledException)
                {
                    Finish(new OperationCanceledException());
                }
                catch (Exception exception)
                {
                    Finish(exception);
                }
            }

            private void Finish(Exception error)
            {
                IsActive = false;
                _cancellation.Dispose();
                if (error is OperationCanceledException)
                    _completion.TrySetCanceled();
                else if (error != null)
                    _completion.TrySetException(error);
                else
                    _completion.TrySetResult();
            }
        }
    }
}
