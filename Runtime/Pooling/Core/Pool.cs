using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Pooling
{
    /// <summary>
    /// A pool of reference-type instances: the engine-agnostic core (counting, limits, growth, prewarming,
    /// idle trimming, ownership checks). How an instance is made and reset belongs to the
    /// <see cref="IPoolStrategy{T}"/>. Main thread only; classes only so generic code is shared on IL2CPP and
    /// HybridCLR.
    /// </summary>
    public sealed class Pool<T> : IPool<T>
        where T : class
    {
        private const double TrimCheckIntervalSeconds = 1d;

        private readonly IPoolStrategy<T> _strategy;
        private readonly IPoolClock _clock;
        private readonly IPoolViolationHandler _violations;
        private readonly List<IdleEntry> _inactive = new();
        private readonly HashSet<T> _activeSet;
        private readonly HashSet<T> _inactiveSet;
        private readonly bool _checks;
        private readonly bool _createOnRent;

        private int _created;
        private int _active;
        private int _peakActive;
        private int _rentCount;
        private int _returnCount;
        private int _misses;
        private int _expansions;
        private int _rejected;
        private int _destroyed;
        private double _nextTrimAt;
        private bool _disposed;

        public PoolId Id { get; }

        public string Name { get; }

        public PoolSettings Settings { get; }

        public PoolStats Stats => new(
            _created, _active, _inactive.Count, _peakActive, _rentCount, _returnCount,
            _misses, _expansions, _rejected, _destroyed);

        /// <param name="createOnRent">When false the pool never creates instances itself: a rent with no idle
        /// instance returns nothing and prewarming throws. The owner creates them (through the shared
        /// spawner) and hands them over with <see cref="Adopt"/>.</param>
        public Pool(
            PoolId id,
            IPoolStrategy<T> strategy,
            PoolSettings settings = null,
            string name = null,
            IPoolClock clock = null,
            bool? safetyChecks = null,
            IPoolViolationHandler violationHandler = null,
            bool createOnRent = true)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
            Settings = settings ?? PoolSettings.Default;
            Id = id;
            Name = string.IsNullOrEmpty(name) ? (id.IsEmpty ? typeof(T).Name : id.Value) : name;
            _clock = clock ?? StopwatchPoolClock.Instance;
            _checks = safetyChecks ?? PoolDefaults.SafetyChecks;
            _violations = violationHandler ?? PoolDefaults.ViolationHandler;
            _createOnRent = createOnRent;
            _activeSet = new(ReferenceComparer.Instance);
            if (_checks)
            {
                _inactiveSet = new(ReferenceComparer.Instance);
            }

            PoolRegistry.Add(this);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            PoolRegistry.Remove(this);
            List<Exception> failures = null;
            if (_checks && _active > 0)
            {
                try
                {
                    Report(PoolViolationKind.LeakedOnDispose, $"{_active} instance(s) were still rented when the pool was disposed.");
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            var inactive = new List<IdleEntry>(_inactive);
            _inactive.Clear();
            _inactiveSet?.Clear();
            foreach (var entry in inactive)
            {
                try
                {
                    DestroyItem(entry.Item);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            try
            {
                (_strategy as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }

            ThrowFailures(failures, "One or more errors occurred while disposing the pool.");
        }

        public T Rent()
        {
            if (TryRentCore(out var item))
                return item;

            if (_disposed || !_createOnRent)
                return null;

            if (Settings.ExhaustedBehavior == PoolExhaustedBehavior.Throw)
                throw new PoolExhaustedException(Name, Settings.MaxActive);

            return null;
        }

        public bool TryRent(out T item) => TryRentCore(out item);

        public void Return(T item)
        {
            if (item == null)
                return;

            if (!_activeSet.Remove(item))
            {
                ReportBadReturn(item);
                return;
            }

            _active--;
            _returnCount++;
            bool isValid;
            try
            {
                isValid = _strategy.IsValid(item);
            }
            catch (Exception exception)
            {
                DestroyAfterFailure(item, exception);
                return;
            }

            if (!isValid)
            {
                DestroyItem(item);
                return;
            }

            try
            {
                _strategy.OnReturn(item);
            }
            catch (Exception exception)
            {
                DestroyAfterFailure(item, exception);
                return;
            }

            if (_disposed || (Settings.MaxRetained > 0 && _inactive.Count >= Settings.MaxRetained))
            {
                DestroyItem(item);
                return;
            }

            AddIdle(item);
        }

        /// <summary>Takes an instance created outside the pool as an idle member; the next rent hands it out.</summary>
        public void Adopt(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (_disposed)
            {
                Report(PoolViolationKind.UseAfterDispose, "Adopt was called on a disposed pool.");
                _strategy.OnDestroy(item);
                return;
            }

            _created++;
            if (_checks)
                PoolOwnership.Register(item, this);

            try
            {
                _strategy.OnReturn(item);
            }
            catch (Exception exception)
            {
                DestroyAfterFailure(item, exception);
            }

            if (_disposed || (Settings.MaxRetained > 0 && _inactive.Count >= Settings.MaxRetained))
            {
                DestroyItem(item);
                return;
            }

            AddIdle(item);
        }

        public PoolLease<T> Lease() => new(this, Rent());

        public bool PrewarmStep(int targetInactive, double deadlineSeconds)
        {
            if (_disposed)
                return true;

            ThrowIfCreatesNothing();

            var target = Settings.MaxRetained > 0 ? Math.Min(targetInactive, Settings.MaxRetained) : targetInactive;
            while (_inactive.Count < target)
            {
                AddIdle(CreateInstance());
                if (_clock.Now >= deadlineSeconds)
                    break;
            }

            return _inactive.Count >= target;
        }

        /// <summary>Creates idle instances immediately. Use it only where a frame spike is acceptable.</summary>
        public void Prewarm(int targetInactive) => PrewarmStep(targetInactive, double.MaxValue);

        public async UniTask PrewarmAsync(
            int targetInactive,
            double budgetMillisecondsPerFrame,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            var startInactive = _inactive.Count;
            var total = Math.Max(1, targetInactive - startInactive);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deadline = _clock.Now + (budgetMillisecondsPerFrame / 1000d);
                var done = PrewarmStep(targetInactive, deadline);
                progress?.Report(done ? 1f : Math.Min(1f, (float)(_inactive.Count - startInactive) / total));
                if (done)
                    return;

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public async UniTask PrewarmAsync(int targetInactive, int instancesPerFrame, CancellationToken cancellationToken = default)
        {
            ThrowIfCreatesNothing();
            if (instancesPerFrame < 1)
                throw new ArgumentOutOfRangeException(nameof(instancesPerFrame));

            var target = Settings.MaxRetained > 0 ? Math.Min(targetInactive, Settings.MaxRetained) : targetInactive;
            while (!_disposed && _inactive.Count < target)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var index = 0; index < instancesPerFrame && _inactive.Count < target; index++)
                    AddIdle(CreateInstance());

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public int Trim(int keep)
        {
            var remove = _inactive.Count - Math.Max(0, keep);
            if (remove <= 0)
                return 0;

            var removed = _inactive.GetRange(0, remove);
            _inactive.RemoveRange(0, remove);
            List<Exception> failures = null;
            foreach (var entry in removed)
            {
                try
                {
                    DestroyIdle(entry.Item);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            ThrowFailures(failures, "One or more errors occurred while trimming the pool.");
            return remove;
        }

        public void Tick(double nowSeconds)
        {
            var idleSeconds = Settings.IdleTrimSeconds;
            if (_disposed || idleSeconds <= 0f || nowSeconds < _nextTrimAt)
                return;

            _nextTrimAt = nowSeconds + TrimCheckIntervalSeconds;
            var removable = Math.Min(_inactive.Count - Settings.MinRetained, Settings.TrimBatchSize);
            var count = 0;
            while (count < removable && nowSeconds - _inactive[count].ReturnedAt >= idleSeconds)
                count++;

            if (count == 0)
                return;

            var removed = _inactive.GetRange(0, count);
            _inactive.RemoveRange(0, count);
            List<Exception> failures = null;
            foreach (var entry in removed)
            {
                try
                {
                    DestroyIdle(entry.Item);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            ThrowFailures(failures, "One or more errors occurred while trimming the pool.");
        }

        private bool TryRentCore(out T item)
        {
            item = null;
            if (_disposed)
            {
                Report(PoolViolationKind.UseAfterDispose, "Rent was called on a disposed pool.");
                return false;
            }

            if (Settings.MaxActive > 0 && _active >= Settings.MaxActive)
            {
                _misses++;
                _rejected++;
                return false;
            }

            item = PopIdle();
            if (item == null)
            {
                _misses++;
                if (!_createOnRent)
                    return false;

                _expansions++;
                item = CreateInstance();
                var extra = Settings.GrowthBatch - 1;
                for (var index = 0; index < extra; index++)
                {
                    if (Settings.MaxRetained > 0 && _inactive.Count >= Settings.MaxRetained)
                        break;

                    AddIdle(CreateInstance());
                }
            }

            try
            {
                _strategy.OnRent(item);
            }
            catch (Exception exception)
            {
                DestroyAfterFailure(item, exception);
                return false;
            }

            _active++;
            _rentCount++;
            if (_active > _peakActive)
                _peakActive = _active;
            _activeSet.Add(item);
            return true;
        }

        private T PopIdle()
        {
            while (_inactive.Count > 0)
            {
                var last = _inactive.Count - 1;
                var item = _inactive[last].Item;
                _inactive.RemoveAt(last);
                _inactiveSet?.Remove(item);
                bool isValid;
                try
                {
                    isValid = _strategy.IsValid(item);
                }
                catch (Exception exception)
                {
                    DestroyAfterFailure(item, exception);
                    return null;
                }

                if (isValid)
                    return item;

                DestroyItem(item);
            }

            return null;
        }

        private void ThrowIfCreatesNothing()
        {
            if (!_createOnRent)
                throw new InvalidOperationException("Pool '" + Name + "' creates nothing; its owner adds instances with Adopt.");
        }

        private T CreateInstance()
        {
            var item = _strategy.Create();
            _created++;
            if (_checks)
                PoolOwnership.Register(item, this);

            return item;
        }

        private void AddIdle(T item)
        {
            _inactive.Add(new IdleEntry(item, Settings.IdleTrimSeconds > 0f ? _clock.Now : 0d));
            _inactiveSet?.Add(item);
        }

        // Idle entries are removed from the list by the caller; this only releases the instance.
        private void DestroyIdle(T item)
        {
            _inactiveSet?.Remove(item);
            DestroyItem(item);
        }

        private void DestroyItem(T item)
        {
            try
            {
                _strategy.OnDestroy(item);
            }
            finally
            {
                _destroyed++;
                if (_checks)
                    PoolOwnership.Unregister(item);
            }
        }

        private void DestroyAfterFailure(T item, Exception failure)
        {
            try
            {
                DestroyItem(item);
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException("A pool strategy callback and cleanup both failed.", failure, cleanupFailure);
            }

            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void ThrowFailures(List<Exception> failures, string message)
        {
            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(message, failures);
        }

        private void ReportBadReturn(T item)
        {
            if (_inactiveSet != null && _inactiveSet.Contains(item))
            {
                Report(PoolViolationKind.DoubleReturn, $"'{item}' was returned twice.");
                return;
            }

            if (_inactiveSet == null)
            {
                foreach (var entry in _inactive)
                {
                    if (!ReferenceEquals(entry.Item, item))
                        continue;

                    Report(PoolViolationKind.DoubleReturn, $"'{item}' was returned twice.");
                    return;
                }
            }

            var owner = PoolOwnership.OwnerOf(item);
            if (owner != null && !ReferenceEquals(owner, this))
                Report(PoolViolationKind.ForeignInstance, $"'{item}' belongs to pool '{owner.Name}'.");
            else
                Report(PoolViolationKind.ForeignInstance, $"'{item}' was not rented from this pool.");
        }

        private void Report(PoolViolationKind kind, string detail) => _violations?.OnViolation(this, kind, detail);

        private readonly struct IdleEntry
        {
            public T Item { get; }

            public double ReturnedAt { get; }

            public IdleEntry(T item, double returnedAt)
            {
                Item = item;
                ReturnedAt = returnedAt;
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new();

            public bool Equals(T left, T right) => ReferenceEquals(left, right);

            public int GetHashCode(T item) => ReferenceEquals(item, null) ? 0 : RuntimeHelpers.GetHashCode(item);
        }
    }
}
