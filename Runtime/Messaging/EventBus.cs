using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public sealed class EventBus : IEventBus
    {
        // A publish normally repeats the same static event type, so its handler snapshot is reused instead of being
        // rebuilt from every subscribed type. The bound keeps many one-off event types from growing it without limit;
        // hitting it clears the cache, which costs one rebuild per type until it fills again.
        // shortcut: no per-entry eviction, a bus that publishes more than this many distinct event types churns the
        // whole cache; switch to an LRU or a WeakReference table if that becomes a real workload.
        private const int SnapshotCacheLimit = 64;

        private readonly Dictionary<Type, List<Registration>> _handlers = new();
        private readonly Dictionary<Type, Registration[]> _snapshots = new();
        private readonly object _gate = new();
        private readonly EventDispatchMode _mode;

        public EventBus() : this(MessagingOptions.Default)
        {
        }

        public EventBus(MessagingOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            _mode = options.Events;
        }

        public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            lock (_gate)
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var registrations))
                {
                    registrations = new List<Registration>();
                    _handlers.Add(typeof(TEvent), registrations);
                }

                var registration = new Registration(handler);
                registrations.Add(registration);
                _snapshots.Clear();
                return new Subscription(() => Unsubscribe(typeof(TEvent), registration));
            }
        }

        public UniTask PublishAsync<TEvent>(
            TEvent message,
            CancellationToken cancellationToken = default)
        {
            var snapshot = GetSnapshot(typeof(TEvent));
            if (snapshot.Length == 0)
                return UniTask.CompletedTask;

            if (_mode == EventDispatchMode.Sequential)
                return PublishSequentialAsync(snapshot, message, cancellationToken);

            var tasks = new List<UniTask>(snapshot.Length);
            List<Exception> failures = null;
            foreach (var registration in snapshot)
            {
                // A cancellation noticed before a handler starts stops the handlers that have not run yet; the ones
                // already started are observed below.
                if (cancellationToken.IsCancellationRequested)
                {
                    AbandonStarted(tasks, failures);
                    throw new OperationCanceledException(cancellationToken);
                }

                try
                {
                    tasks.Add(((IEventHandler<TEvent>)registration.Handler).HandleAsync(message, cancellationToken));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AbandonStarted(tasks, failures);
                    throw;
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            // The token cannot be cancelled, so there is nothing to race and nothing to synchronise.
            return cancellationToken.CanBeCanceled
                ? PublishAllCancellableAsync(tasks, new FailureCollector(failures), cancellationToken)
                : PublishAllAsync(tasks, failures);
        }

        // Returns the handler view for one static event type, shared until a subscription changes. Handlers may
        // unsubscribe while a publish runs, hence a copy instead of the live list.
        private Registration[] GetSnapshot(Type eventType)
        {
            lock (_gate)
            {
                if (_snapshots.TryGetValue(eventType, out var cached))
                    return cached;

                var count = 0;
                foreach (var pair in _handlers)
                {
                    if (pair.Key.IsAssignableFrom(eventType))
                        count += pair.Value.Count;
                }

                Registration[] snapshot;
                if (count == 0)
                {
                    snapshot = Array.Empty<Registration>();
                }
                else
                {
                    snapshot = new Registration[count];
                    var filled = 0;
                    foreach (var pair in _handlers)
                    {
                        if (!pair.Key.IsAssignableFrom(eventType))
                            continue;

                        pair.Value.CopyTo(snapshot, filled);
                        filled += pair.Value.Count;
                    }
                }

                if (_snapshots.Count >= SnapshotCacheLimit)
                    _snapshots.Clear();

                _snapshots.Add(eventType, snapshot);
                return snapshot;
            }
        }

        // One handler at a time in subscription order; every handler still gets the event whatever an earlier one did.
        private async UniTask PublishSequentialAsync<TEvent>(Registration[] registrations, TEvent message, CancellationToken cancellationToken)
        {
            List<Exception> failures = null;
            foreach (var registration in registrations)
            {
                // A cancelled publish stops here instead of starting handlers that would only be cancelled themselves;
                // failures collected before this point are still reported once.
                if (cancellationToken.IsCancellationRequested)
                {
                    ReportAll(failures);
                    throw new OperationCanceledException(cancellationToken);
                }

                // Earlier handlers may have awaited for a while: a subscription disposed meanwhile (its view was closed)
                // must not be called afterwards.
                if (!registration.IsSubscribed)
                    continue;

                try
                {
                    await ((IEventHandler<TEvent>)registration.Handler).HandleAsync(message, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    ReportAll(failures);
                    throw;
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            EventHandlerFailures.ThrowIfAny(failures);
        }

        // Every handler gets the event whatever an earlier one did; a failure is reported once all of them have run.
        private static async UniTask PublishAllAsync(List<UniTask> tasks, List<Exception> failures)
        {
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            EventHandlerFailures.ThrowIfAny(failures);
        }

        // Same contract, but a cancelled publish returns at once instead of waiting for handlers still running; a
        // failure one raises afterwards goes to UniTask's unobserved path, so it is reported instead of being lost.
        private static async UniTask PublishAllCancellableAsync(
            List<UniTask> tasks,
            FailureCollector failures,
            CancellationToken cancellationToken)
        {
            var remaining = tasks.Count;
            var allDone = new UniTaskCompletionSource();
            if (remaining == 0)
                allDone.TrySetResult();

            async UniTaskVoid ObserveAsync(UniTask task)
            {
                try
                {
                    await task;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // The cancelled publish is already returning; this handler only obeyed it.
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                finally
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                        allDone.TrySetResult();
                }
            }

            foreach (var task in tasks)
                ObserveAsync(task).Forget();

            try
            {
                await allDone.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The publish stops waiting here: report what it has already collected and everything later.
                failures.Abandon();
                throw;
            }

            EventHandlerFailures.ThrowIfAny(failures.Take());
        }

        // The publish ends without waiting: already started handlers keep running and report their own failures.
        private static void AbandonStarted(List<UniTask> tasks, List<Exception> failures)
        {
            foreach (var started in tasks)
                started.Forget();

            ReportAll(failures);
        }

        private static void ReportAll(List<Exception> failures)
        {
            if (failures == null)
                return;

            foreach (var failure in failures)
                Report(failure);
        }

        private static void Report(Exception failure)
        {
            // UniTaskScheduler drops OperationCanceledException by default. A handler's own cancellation was already
            // classified as a failure here, so report it as the cause of an aggregate instead of changing that option.
            if (failure is OperationCanceledException)
                failure = new AggregateException("An event handler failed with a cancellation that was not the publish's.", failure);

            UniTask.FromException(failure).Forget();
        }

        // Handlers may finish on different threads while the publish cancels, so the list and the switch from
        // aggregating to reporting are guarded; every failure is reported or aggregated exactly once.
        private sealed class FailureCollector
        {
            private readonly object _gate = new();
            private List<Exception> _failures;
            private bool _abandoned;

            public FailureCollector(List<Exception> failures)
            {
                _failures = failures;
            }

            public void Add(Exception failure)
            {
                lock (_gate)
                {
                    if (!_abandoned)
                    {
                        (_failures ??= new List<Exception>()).Add(failure);
                        return;
                    }
                }

                Report(failure);
            }

            public void Abandon()
            {
                List<Exception> pending;
                lock (_gate)
                {
                    if (_abandoned)
                        return;

                    _abandoned = true;
                    pending = _failures;
                    _failures = null;
                }

                ReportAll(pending);
            }

            public List<Exception> Take()
            {
                lock (_gate)
                    return _failures;
            }
        }

        private void Unsubscribe(Type eventType, Registration registration)
        {
            lock (_gate)
            {
                registration.IsSubscribed = false;
                _snapshots.Clear();

                if (!_handlers.TryGetValue(eventType, out var registrations))
                    return;

                registrations.Remove(registration);
                if (registrations.Count == 0)
                    _handlers.Remove(eventType);
            }
        }

        // One per subscription: a handler subscribed twice has two registrations, and disposing one leaves the other.
        private sealed class Registration
        {
            public object Handler { get; }

            // Written while a publish may be awaiting an earlier handler on another thread, so the sequential loop
            // must observe it. volatile provides the acquire/release pair; it does not make handler execution itself
            // thread-safe.
            private volatile bool _isSubscribed = true;

            public bool IsSubscribed
            {
                get => _isSubscribed;
                set => _isSubscribed = value;
            }

            public Registration(object handler)
            {
                Handler = handler;
            }
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                var dispose = _dispose;
                _dispose = null;
                dispose?.Invoke();
            }
        }
    }

    /// <summary>Reports what failed among an event's handlers once every one of them has had the event.</summary>
    public static class EventHandlerFailures
    {
        public static void ThrowIfAny(List<Exception> failures)
        {
            if (failures == null || failures.Count == 0)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Several event handlers failed.", failures);
        }
    }
}