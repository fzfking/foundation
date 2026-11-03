using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.Timing;

namespace Foundation.Scheduling
{
    public interface IScheduledHandle : IDisposable
    {
        bool IsActive { get; }
    }

    public interface IScheduler
    {
        IScheduledHandle ScheduleOnce(float delaySeconds, Action callback);

        /// <summary>
        /// Repeats on a fixed delay: a full interval starts after every callback, so a hitch fires once
        /// instead of bursting. Fixed rate is the separate <see cref="IFixedRateScheduler"/> capability.
        /// </summary>
        IScheduledHandle ScheduleRepeating(float intervalSeconds, Action callback);
    }

    /// <summary>What a fixed-rate callback does with callbacks a tick cannot run within its per-tick cap.</summary>
    public enum FixedRateBacklogPolicy
    {
        /// <summary>Drops them and resumes at the next interval boundary, keeping the timer's phase.</summary>
        Bounded,

        /// <summary>Keeps them as debt and runs them on later ticks, still at most the cap per tick.</summary>
        Preserve
    }

    /// <summary>
    /// Optional capability of an <see cref="IScheduler"/>: repeating callbacks that keep the interval as a rate
    /// instead of restarting it after every callback. Separate from <see cref="IScheduler"/> so existing
    /// implementations keep compiling; use <see cref="SchedulerExtensions.ScheduleFixedRateRepeating"/> to call
    /// it through an <see cref="IScheduler"/>.
    /// </summary>
    public interface IFixedRateScheduler
    {
        /// <summary>
        /// Schedules <paramref name="callback"/> every <paramref name="intervalSeconds"/> of unscaled time,
        /// measured between due times rather than from the callback. A tick runs at most
        /// <paramref name="maxCallbacksPerTick"/> of them; callbacks it cannot run follow
        /// <paramref name="backlogPolicy"/>. <see cref="FixedRateBacklogPolicy.Preserve"/> keeps debt for later
        /// ticks; a debt the interval cannot reduce (below the accumulator's precision) deactivates that timer
        /// and reports <see cref="InvalidOperationException"/> with the tick's other callback failures, instead
        /// of dropping the debt silently.
        /// </summary>
        IScheduledHandle ScheduleFixedRateRepeating(
            float intervalSeconds,
            Action callback,
            int maxCallbacksPerTick,
            FixedRateBacklogPolicy backlogPolicy);
    }

    /// <summary>
    /// Runs callbacks after a delay or on an interval of unscaled time. <see cref="ScheduleRepeating"/> is
    /// fixed-delay: a full interval restarts after each callback, so a hitch never bursts. A timer that must
    /// keep its rate uses <see cref="ScheduleFixedRateRepeating"/> with an explicit per-tick cap.
    /// A callback that throws does not stop the others that tick; its failure is rethrown after they all ran.
    /// Registered once in the application scope; a game context uses a <see cref="ScopedScheduler"/> so its
    /// callbacks end with it.
    /// </summary>
    public sealed class Scheduler : IScheduler, IFixedRateScheduler, ITickableEntryPoint, IDisposable
    {
        private readonly ITimeProvider _time;
        private readonly ITimeIntegrityMonitor _integrityMonitor;
        private readonly List<Entry> _entries = new();

        private DateTime _lastUtc;
        private bool _hasLastUtc;
        private bool _disposed;
        private bool _isTicking;

        public Scheduler(ITimeProvider time, ITimeIntegrityMonitor integrityMonitor = null)
        {
            _time = time ?? throw new ArgumentNullException(nameof(time));
            _integrityMonitor = integrityMonitor;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var entry in _entries)
                entry.Dispose();
            _entries.Clear();
        }

        public IScheduledHandle ScheduleOnce(float delaySeconds, Action callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Scheduler));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (!float.IsFinite(delaySeconds))
                throw new ArgumentOutOfRangeException(nameof(delaySeconds), "Delay must be finite.");
            var entry = new Entry(delaySeconds, callback, EntryMode.Once, interval: 0f);
            _entries.Add(entry);
            return entry;
        }

        public IScheduledHandle ScheduleRepeating(float intervalSeconds, Action callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Scheduler));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (!float.IsFinite(intervalSeconds) || intervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be finite and greater than zero.");
            var entry = new Entry(intervalSeconds, callback, EntryMode.FixedDelay, intervalSeconds);
            _entries.Add(entry);
            return entry;
        }

        public IScheduledHandle ScheduleFixedRateRepeating(
            float intervalSeconds,
            Action callback,
            int maxCallbacksPerTick,
            FixedRateBacklogPolicy backlogPolicy)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(Scheduler));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));
            if (!float.IsFinite(intervalSeconds) || intervalSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds), "Interval must be finite and greater than zero.");
            if (maxCallbacksPerTick < 1)
                throw new ArgumentOutOfRangeException(nameof(maxCallbacksPerTick), "At least one callback per tick is required.");
            if (backlogPolicy != FixedRateBacklogPolicy.Bounded && backlogPolicy != FixedRateBacklogPolicy.Preserve)
                throw new ArgumentOutOfRangeException(nameof(backlogPolicy));
            var entry = new Entry(
                intervalSeconds,
                callback,
                EntryMode.FixedRate,
                intervalSeconds,
                maxCallbacksPerTick,
                backlogPolicy);
            _entries.Add(entry);
            return entry;
        }

        public void Tick()
        {
            // A callback that ticks again is ignored: this frame's time has already been advanced, and
            // re-entering would fire the same timers twice in one frame.
            if (_disposed || _isTicking)
                return;

            var deltaTime = _time.UnscaledDeltaTime;
            if (!float.IsFinite(deltaTime))
                throw new InvalidOperationException("The time provider returned a non-finite unscaled delta time.");

            List<Exception> failures = null;
            _isTicking = true;
            try
            {
                ObserveIntegrity(deltaTime);
                for (var index = _entries.Count - 1; index >= 0 && !_disposed; index--)
                {
                    var entry = _entries[index];
                    if (!entry.IsActive)
                    {
                        _entries.RemoveAt(index);
                        continue;
                    }

                    entry.Remaining -= deltaTime;

                    switch (entry.Mode)
                    {
                        case EntryMode.Once:
                            if (entry.Remaining > 0f)
                                continue;
                            entry.Dispose();
                            _entries.RemoveAt(index);
                            Invoke(entry, ref failures);
                            break;

                        case EntryMode.FixedDelay:
                            if (entry.Remaining > 0f)
                                continue;
                            // A full interval restarts after every call: a hitch or a backgrounded app
                            // never causes a burst of catch-up callbacks. Fixed rate is opt-in.
                            entry.Remaining = entry.Interval;
                            Invoke(entry, ref failures);
                            break;

                        case EntryMode.FixedRate:
                            InvokeFixedRate(entry, ref failures);
                            break;
                    }
                }
            }
            finally
            {
                _isTicking = false;
            }

            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Several scheduled callbacks failed.", failures);
        }

        private static void Invoke(Entry entry, ref List<Exception> failures)
        {
            try
            {
                entry.Callback();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }
        }

        private static void InvokeFixedRate(Entry entry, ref List<Exception> failures)
        {
            // A non-finite debt has no phase to keep and can never drain.
            if (!double.IsFinite(entry.Remaining))
            {
                FailFixedRate(
                    entry,
                    ref failures,
                    "A fixed-rate timer was deactivated: its accumulated time debt is outside the representable range.");
                return;
            }

            // Preserve may not silently drop debt: when the interval is below the precision of the debt,
            // adding it changes nothing and the timer would fire the cap on every frame forever. Fault only
            // this timer, so the rest of the tick and the failures already collected still run.
            if (entry.BacklogPolicy == FixedRateBacklogPolicy.Preserve
                && entry.Remaining <= 0d
                && entry.Remaining + entry.Interval <= entry.Remaining)
            {
                FailFixedRate(
                    entry,
                    ref failures,
                    $"A fixed-rate timer was deactivated: it cannot drain {-entry.Remaining:R} s of debt with an " +
                    $"interval of {entry.Interval:R} s because the interval is below the precision of the debt. " +
                    $"Use {nameof(FixedRateBacklogPolicy.Bounded)} to drop the excess instead.");
                return;
            }

            var fired = 0;
            while (entry.IsActive && entry.Remaining <= 0d && fired < entry.MaxCallbacksPerTick)
            {
                entry.Remaining += entry.Interval;
                fired++;
                Invoke(entry, ref failures);
            }

            if (!entry.IsActive || entry.Remaining > 0d)
                return;
            if (entry.BacklogPolicy == FixedRateBacklogPolicy.Preserve)
                return;

            // Bounded: drop the callbacks this tick could not run and resume at the next interval
            // boundary, so the timer keeps its phase instead of restarting from "now".
            var overdue = -entry.Remaining;
            entry.Remaining = entry.Interval - overdue % entry.Interval;
        }

        private static void FailFixedRate(Entry entry, ref List<Exception> failures, string message)
        {
            // The debt cannot be kept, so stop this timer instead of faulting the tick every frame. The
            // exception says why; it is collected with callback failures and thrown when the tick ends.
            entry.Dispose();
            (failures ??= new List<Exception>()).Add(new InvalidOperationException(message));
        }

        private void ObserveIntegrity(float deltaTime)
        {
            if (_integrityMonitor == null)
                return;

            var now = _time.UtcNow;
            if (_hasLastUtc)
                _integrityMonitor.Observe(deltaTime, now - _lastUtc);

            _lastUtc = now;
            _hasLastUtc = true;
        }

        private enum EntryMode
        {
            Once,
            FixedDelay,
            FixedRate
        }

        private sealed class Entry : IScheduledHandle
        {
            // double keeps a long-running fixed-rate timer's debt and phase meaningful; float stops
            // changing once the debt grows past its mantissa.
            public double Remaining;

            public readonly Action Callback;
            public readonly EntryMode Mode;
            public readonly double Interval;
            public readonly int MaxCallbacksPerTick;
            public readonly FixedRateBacklogPolicy BacklogPolicy;

            private bool _disposed;

            public bool IsActive => !_disposed;

            public Entry(
                double remaining,
                Action callback,
                EntryMode mode,
                double interval,
                int maxCallbacksPerTick = 1,
                FixedRateBacklogPolicy backlogPolicy = FixedRateBacklogPolicy.Bounded)
            {
                Remaining = remaining;
                Callback = callback;
                Mode = mode;
                Interval = interval;
                MaxCallbacksPerTick = maxCallbacksPerTick;
                BacklogPolicy = backlogPolicy;
            }

            public void Dispose() => _disposed = true;
        }
    }

    public static class SchedulerExtensions
    {
        /// <summary>
        /// Schedules at a fixed rate through <see cref="IFixedRateScheduler"/>. This throws
        /// <see cref="NotSupportedException"/> for an implementation that does not offer it, rather than
        /// silently degrading to the slower fixed-delay <see cref="IScheduler.ScheduleRepeating"/>.
        /// </summary>
        public static IScheduledHandle ScheduleFixedRateRepeating(
            this IScheduler scheduler,
            float intervalSeconds,
            Action callback,
            int maxCallbacksPerTick = 1,
            FixedRateBacklogPolicy backlogPolicy = FixedRateBacklogPolicy.Bounded)
        {
            if (scheduler == null)
                throw new ArgumentNullException(nameof(scheduler));
            if (scheduler is IFixedRateScheduler fixedRateScheduler)
                return fixedRateScheduler.ScheduleFixedRateRepeating(
                    intervalSeconds,
                    callback,
                    maxCallbacksPerTick,
                    backlogPolicy);
            throw new NotSupportedException(
                $"{scheduler.GetType().Name} does not implement {nameof(IFixedRateScheduler)}; fixed-rate repeating callbacks need it.");
        }
    }

    /// <summary>
    /// Schedules on the application's <see cref="Scheduler"/> and cancels what it scheduled when its scope is
    /// disposed, so a context's timers never call into a context that is gone.
    /// </summary>
    public sealed class ScopedScheduler : IScheduler, IFixedRateScheduler, IDisposable
    {
        private readonly IScheduler _scheduler;
        private readonly List<IScheduledHandle> _handles = new();

        private bool _disposed;

        public ScopedScheduler(IScheduler scheduler)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var handle in _handles)
                handle.Dispose();
            _handles.Clear();
        }

        public IScheduledHandle ScheduleOnce(float delaySeconds, Action callback)
            => Track(_scheduler.ScheduleOnce(delaySeconds, callback));

        public IScheduledHandle ScheduleRepeating(float intervalSeconds, Action callback)
            => Track(_scheduler.ScheduleRepeating(intervalSeconds, callback));

        public IScheduledHandle ScheduleFixedRateRepeating(
            float intervalSeconds,
            Action callback,
            int maxCallbacksPerTick,
            FixedRateBacklogPolicy backlogPolicy)
        {
            if (_scheduler is not IFixedRateScheduler fixedRateScheduler)
                throw new NotSupportedException(
                    $"{_scheduler.GetType().Name} does not implement {nameof(IFixedRateScheduler)}; fixed-rate repeating callbacks need it.");

            return Track(fixedRateScheduler.ScheduleFixedRateRepeating(
                intervalSeconds,
                callback,
                maxCallbacksPerTick,
                backlogPolicy));
        }

        private IScheduledHandle Track(IScheduledHandle handle)
        {
            if (_disposed)
            {
                handle.Dispose();
                throw new ObjectDisposedException(nameof(ScopedScheduler));
            }

            _handles.RemoveAll(tracked => !tracked.IsActive);
            _handles.Add(handle);
            return handle;
        }
    }

    public static class SchedulingServiceRegistryExtensions
    {
        public static void RegisterScheduler(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            // The integrity monitor is optional: a factory honours that, a constructor parameter could not.
            registry.RegisterEntryPoint(
                resolver => new Scheduler(
                    resolver.Resolve<ITimeProvider>(),
                    resolver.TryResolve(out ITimeIntegrityMonitor monitor) ? monitor : null)).AsSelf();
        }

        /// <summary><see cref="RegisterScheduler"/>, chainable. Needs an <see cref="ITimeProvider"/>.</summary>
        public static IServiceRegistry UseScheduler(this IServiceRegistry registry)
        {
            registry.RegisterScheduler();
            return registry;
        }

        /// <summary><see cref="RegisterScopedScheduler"/>, chainable.</summary>
        public static IServiceRegistry UseScopedScheduler(this IServiceRegistry registry)
        {
            registry.RegisterScopedScheduler();
            return registry;
        }

        /// <summary>
        /// This scope's <see cref="IScheduler"/>: schedules on the application's and cancels its callbacks
        /// when the scope is disposed. The same wrapper answers <see cref="IFixedRateScheduler"/>, so a timer
        /// scheduled through either capability still leaves with the scope.
        /// </summary>
        public static void RegisterScopedScheduler(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<ScopedScheduler>(
                    resolver => new ScopedScheduler(resolver.Resolve<Scheduler>()),
                    ServiceLifetime.Singleton)
                .As<IScheduler>()
                .As<IFixedRateScheduler>();
        }
    }
}
