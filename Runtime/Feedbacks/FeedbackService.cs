using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Audio;
using Foundation.Haptics;
using Foundation.Logging;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Feedbacks
{
    public interface IFeedbackService
    {
        FeedbackHandle Play(FeedbackId id, FeedbackContext context = default);
        UniTask PlayAsync(FeedbackId id, FeedbackContext context = default, CancellationToken cancellationToken = default);
    }

    public sealed class FeedbackHandle
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly UniTaskCompletionSource _completion = new();

        public bool IsPlaying { get; private set; } = true;

        public UniTask Completion => _completion.Task;

        internal CancellationToken Token => _cancellation.Token;

        internal FeedbackHandle(CancellationTokenSource cancellation) => _cancellation = cancellation;

        public void Cancel()
        {
            if (IsPlaying)
                _cancellation.Cancel();
        }

        internal void Finish(Exception error)
        {
            IsPlaying = false;
            if (error is OperationCanceledException)
                _completion.TrySetCanceled();
            else if (error != null)
                _completion.TrySetException(error);
            else
                _completion.TrySetResult();
            _cancellation.Dispose();
        }
    }

    public sealed class FeedbackService : IFeedbackService, IDisposable
    {
        private readonly Dictionary<FeedbackId, FeedbackDefinition> _definitions = new();
        private readonly Dictionary<(FeedbackId Id, GameObject Target), FeedbackHandle> _playing = new();
        private readonly FeedbackServices _services;
        private readonly IAppLogger _logger;
        private bool _disposed;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public static FeedbackService DebugCurrent { get; private set; }
#endif

        public FeedbackService(FeedbackLibrary library, IAudioService audio = null, IHapticsService haptics = null, IMotionService motion = null, IAppLogger logger = null)
            : this(library?.Definitions ?? throw new ArgumentNullException(nameof(library)), audio, haptics, motion, logger) { }

        public FeedbackService(IReadOnlyList<FeedbackDefinition> definitions, IAudioService audio = null, IHapticsService haptics = null, IMotionService motion = null, IAppLogger logger = null)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            _services = new(audio, haptics, motion);
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            foreach (var definition in definitions)
            {
                if (definition == null || definition.Id.IsEmpty)
                    throw new ArgumentException("Feedback id is required.", nameof(definitions));
                if (!_definitions.TryAdd(definition.Id, definition))
                    throw new ArgumentException($"Duplicate feedback id: {definition.Id}.", nameof(definitions));
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            DebugCurrent = this;
#endif
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (ReferenceEquals(DebugCurrent, this))
                DebugCurrent = null;
#endif
            // Cancelling can finish a feedback synchronously, which removes it from _playing.
            foreach (var handle in new List<FeedbackHandle>(_playing.Values))
                handle.Cancel();
            _playing.Clear();
        }

        public FeedbackHandle Play(FeedbackId id, FeedbackContext context = default)
            => Start(id, context, CancellationToken.None);

        public UniTask PlayAsync(FeedbackId id, FeedbackContext context = default, CancellationToken cancellationToken = default)
            => Start(id, context, cancellationToken).Completion;

        private FeedbackHandle Start(FeedbackId id, FeedbackContext context, CancellationToken cancellationToken)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FeedbackService));
            if (id.IsEmpty || !_definitions.TryGetValue(id, out var definition))
                throw new ArgumentException($"Unknown feedback id: {id}.", nameof(id));

            var key = (id, context.Target);
            _playing.TryGetValue(key, out var previous);
            if (previous is { IsPlaying: true } && definition.RepeatPolicy == FeedbackRepeatPolicy.IgnoreWhilePlaying)
                return previous;

            CancellationTokenSource linked;
            if (context.HasTarget)
            {
                var targetLifetime = RequiresTargetLifetime(definition)
                    ? GetTargetLifetime(context.Target).DisabledToken
                    : context.Target.GetCancellationTokenOnDestroy();
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, targetLifetime);
            }
            else
            {
                linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            }
            var handle = new FeedbackHandle(linked);
            _playing[key] = handle;
            previous?.Cancel();
            RunAsync(definition, context, key, handle, previous).Forget();
            return handle;
        }

        private static bool RequiresTargetLifetime(FeedbackDefinition definition)
        {
            foreach (var step in definition.Steps)
            {
                if (step?.Action != null && step.Action.RequiresTargetLifetime)
                    return true;
            }
            return false;
        }

        private static FeedbackTargetLifetime GetTargetLifetime(GameObject target)
        {
            if (!target.TryGetComponent<FeedbackTargetLifetime>(out var lifetime))
                lifetime = target.AddComponent<FeedbackTargetLifetime>();
            return lifetime;
        }

        private async UniTask RunAsync(
            FeedbackDefinition definition,
            FeedbackContext context,
            (FeedbackId Id, GameObject Target) key,
            FeedbackHandle handle,
            FeedbackHandle previous)
        {
            Exception error = null;
            try
            {
                if (previous is { IsPlaying: true })
                {
                    try
                    {
                        await previous.Completion;
                    }
                    catch (Exception)
                    {
                        // The previous feedback has reported its own failure; this one starts either way.
                    }
                }

                handle.Token.ThrowIfCancellationRequested();
                var steps = definition.Steps;
                var tasks = new UniTask[steps.Count];
                for (var index = 0; index < steps.Count; index++)
                    tasks[index] = RunStepAsync(steps[index], context, handle.Token);
                await UniTask.WhenAll(tasks);
            }
            catch (OperationCanceledException exception)
            {
                error = exception;
            }
            catch (Exception exception)
            {
                error = exception;
                _logger.Log(LogLevel.Error, "Feedback", $"'{definition.Id}' failed: {exception}");
            }
            finally
            {
                if (_playing.TryGetValue(key, out var current) && ReferenceEquals(current, handle))
                    _playing.Remove(key);
                handle.Finish(error);
            }
        }

        private async UniTask RunStepAsync(FeedbackStep step, FeedbackContext context, CancellationToken cancellationToken)
        {
            if (step?.Action == null)
                return;
            if (step.Delay > 0f)
                await UniTask.Delay(TimeSpan.FromSeconds(step.Delay), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            await step.Action.PlayAsync(context, _services, cancellationToken);
        }
    }
}
