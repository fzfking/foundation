using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Pooling
{
    /// <summary>The part of a pool the service, trimming and diagnostics need, independent of the item type.</summary>
    public interface IPool : IDisposable
    {
        PoolId Id { get; }

        string Name { get; }

        PoolStats Stats { get; }

        PoolSettings Settings { get; }

        /// <summary>
        /// Creates idle instances until <paramref name="targetInactive"/> are ready or the clock reaches
        /// <paramref name="deadlineSeconds"/>. Creates at least one when below target, so repeated calls
        /// always progress. Returns true once the target is met.
        /// </summary>
        bool PrewarmStep(int targetInactive, double deadlineSeconds);

        int Trim(int keep);

        /// <summary>Applies idle trimming. Called by the service; throttled internally.</summary>
        void Tick(double nowSeconds);
    }

    public interface IPool<T> : IPool
        where T : class
    {
        /// <summary>Hands out an instance. Returns null (or throws, see <see cref="PoolSettings.ExhaustedBehavior"/>)
        /// when <see cref="PoolSettings.MaxActive"/> is reached.</summary>
        T Rent();

        bool TryRent(out T item);

        /// <summary>Takes an instance back. A duplicate or foreign return is rejected in every build; development
        /// builds add ownership diagnostics.</summary>
        void Return(T item);

        /// <summary>A rented instance that goes back when disposed: <c>using var lease = pool.Lease();</c></summary>
        PoolLease<T> Lease();

        /// <summary>Creates idle instances over several frames, never spending more than
        /// <paramref name="budgetMillisecondsPerFrame"/> per frame (at least one instance per frame).</summary>
        UniTask PrewarmAsync(
            int targetInactive,
            double budgetMillisecondsPerFrame,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default);
    }

    public readonly struct PoolLease<T> : IDisposable
        where T : class
    {
        private readonly LeaseState _state;

        public T Item => _state?.Item;

        public PoolLease(IPool<T> pool, T item)
        {
            _state = new LeaseState(pool, item);
        }

        public void Dispose()
        {
            if (_state != null && _state.TryDispose() && _state.Pool != null && _state.Item != null)
                _state.Pool.Return(_state.Item);
        }

        private sealed class LeaseState
        {
            private int _disposed;

            public IPool<T> Pool { get; }
            public T Item { get; }

            public LeaseState(IPool<T> pool, T item)
            {
                Pool = pool;
                Item = item;
            }

            public bool TryDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
        }
    }
}
