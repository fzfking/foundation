using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.Logging;

namespace Foundation.Pooling
{
    public interface IPoolService
    {
        PrefabPool<T> CreatePrefabPool<T>(PoolId id, T prefab, PoolSettings settings = null, IPrefabLease lease = null)
            where T : Object;

        /// <summary>Loads the prefab from <paramref name="source"/> and creates the pool. The pool releases the prefab when it is released or the service is disposed.</summary>
        UniTask<PrefabPool<T>> CreatePrefabPoolAsync<T>(
            PoolId id,
            IPrefabSource source,
            PoolSettings settings = null,
            CancellationToken cancellationToken = default)
            where T : Object;

        Pool<T> CreatePool<T>(PoolId id, IPoolStrategy<T> strategy, PoolSettings settings = null)
            where T : class;

        bool TryGet(PoolId id, out IPool pool);

        bool TryGet<T>(PoolId id, out IPool<T> pool)
            where T : class;

        /// <summary>Disposes one pool and frees its id. Rented instances are reported as leaked in development builds.</summary>
        void Release(PoolId id);

        /// <summary>
        /// Prewarms every pool to its <see cref="PoolSettings.PrewarmCount"/> over several frames, spending
        /// at most <paramref name="budgetMillisecondsPerFrame"/> per frame in total (at least one instance
        /// per frame). Use it behind a loading screen.
        /// </summary>
        UniTask PrewarmAllAsync(
            double budgetMillisecondsPerFrame,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default);

        void Collect(List<IPool> results);
    }

    /// <summary>
    /// Owns the pools of one scope: creates them, prewarms them within a frame budget and trims them. The
    /// only ticking object of pooling, so pools have no Update of their own.
    /// </summary>
    public sealed class PoolService : IPoolService, ITickableEntryPoint, IDisposable
    {
        private readonly IGameObjectInjector _injector;
        private readonly IAppLogger _logger;
        private readonly IPoolClock _clock;
        private readonly Dictionary<PoolId, IPool> _pools = new();
        private readonly List<IPool> _ordered = new();
        private readonly Dictionary<PoolId, Transform> _containers = new();
        private readonly HashSet<PoolId> _pending = new();
        private Transform _root;
        private bool _disposed;

        public PoolService(IGameObjectInjector injector)
            : this(injector, StopwatchPoolClock.Instance)
        {
        }

        public PoolService(IGameObjectInjector injector, IPoolClock clock, IAppLogger logger = null)
        {
            _injector = injector;
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            List<Exception> failures = null;
            for (var index = _ordered.Count - 1; index >= 0; index--)
            {
                try
                {
                    _ordered[index].Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            _ordered.Clear();
            _pools.Clear();
            _containers.Clear();
            _pending.Clear();
            if (_root != null)
            {
                try
                {
                    DestroyObject(_root.gameObject);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            if (failures != null)
                throw new AggregateException("One or more errors occurred while disposing the pool service.", failures);
        }

        public PrefabPool<T> CreatePrefabPool<T>(PoolId id, T prefab, PoolSettings settings = null, IPrefabLease lease = null)
            where T : Object
        {
            ThrowIfCannotAdd(id);
            var container = GetContainer(id);
            PrefabPool<T> pool = null;
            try
            {
                pool = new PrefabPool<T>(id, prefab, settings, container, _injector, lease, clock: _clock, logger: _logger);
                Add(id, pool);
                return pool;
            }
            catch
            {
                try
                {
                    pool?.Dispose();
                }
                finally
                {
                    DestroyContainer(id, container);
                }

                throw;
            }
        }

        public async UniTask<PrefabPool<T>> CreatePrefabPoolAsync<T>(
            PoolId id,
            IPrefabSource source,
            PoolSettings settings = null,
            CancellationToken cancellationToken = default)
            where T : Object
        {
            ThrowIfCannotAdd(id);
            _pending.Add(id);
            Transform container = null;
            PrefabPool<T> pool = null;
            try
            {
                container = GetContainer(id);
                pool = await PrefabPool<T>.CreateAsync(id, source, settings, container, _injector, null, _clock, cancellationToken, _logger);
                if (_disposed || _pools.ContainsKey(id))
                    throw new InvalidOperationException($"Pool '{id}' was created twice or the service was disposed while loading.");

                Add(id, pool);
                return pool;
            }
            catch
            {
                try
                {
                    pool?.Dispose();
                }
                finally
                {
                    DestroyContainer(id, container);
                }

                throw;
            }
            finally
            {
                _pending.Remove(id);
            }
        }

        public Pool<T> CreatePool<T>(PoolId id, IPoolStrategy<T> strategy, PoolSettings settings = null)
            where T : class
        {
            ThrowIfCannotAdd(id);
            var pool = new Pool<T>(id, strategy, settings, clock: _clock);
            Add(id, pool);
            return pool;
        }

        public bool TryGet(PoolId id, out IPool pool) => _pools.TryGetValue(id, out pool);

        public bool TryGet<T>(PoolId id, out IPool<T> pool)
            where T : class
        {
            if (_pools.TryGetValue(id, out var found) && found is IPool<T> typed)
            {
                pool = typed;
                return true;
            }

            pool = null;
            return false;
        }

        public void Release(PoolId id)
        {
            if (!_pools.Remove(id, out var pool))
                return;

            _ordered.Remove(pool);
            try
            {
                pool.Dispose();
            }
            finally
            {
                if (_containers.TryGetValue(id, out var container))
                    DestroyContainer(id, container);
            }
        }

        public async UniTask PrewarmAllAsync(
            double budgetMillisecondsPerFrame,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            var total = RemainingPrewarm();
            if (total == 0)
            {
                progress?.Report(1f);
                return;
            }

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deadline = _clock.Now + (budgetMillisecondsPerFrame / 1000d);
                var createdAny = false;
                foreach (var pool in _ordered)
                {
                    var target = pool.Settings.PrewarmCount;
                    if (pool.Stats.Inactive >= target)
                        continue;

                    // Past the budget: stop for this frame, unless nothing was created yet (progress is guaranteed).
                    if (createdAny && _clock.Now >= deadline)
                        break;

                    pool.PrewarmStep(target, deadline);
                    createdAny = true;
                }

                var remaining = RemainingPrewarm();
                progress?.Report(1f - ((float)remaining / total));
                if (remaining == 0)
                    return;

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        public void Collect(List<IPool> results) => results.AddRange(_ordered);

        public void Tick()
        {
            var now = _clock.Now;
            foreach (var pool in _ordered)
                pool.Tick(now);
        }

        private int RemainingPrewarm()
        {
            var remaining = 0;
            foreach (var pool in _ordered) 
                remaining += Math.Max(0, Math.Min(pool.Settings.PrewarmCount, CapOf(pool)) - pool.Stats.Inactive);

            return remaining;
        }

        private static int CapOf(IPool pool) => pool.Settings.MaxRetained > 0 ? pool.Settings.MaxRetained : int.MaxValue;

        private void ThrowIfCannotAdd(PoolId id)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PoolService));

            if (id.IsEmpty)
                throw new ArgumentException("A pool needs an id.", nameof(id));

            if (_pools.ContainsKey(id) || _pending.Contains(id))
                throw new InvalidOperationException($"A pool '{id}' already exists.");
        }

        private void Add(PoolId id, IPool pool)
        {
            _pools.Add(id, pool);
            _ordered.Add(pool);
        }

        private Transform GetContainer(PoolId id)
        {
            if (_root == null)
            {
                var root = new GameObject("[Pools]");
                if (Application.isPlaying)
                    Object.DontDestroyOnLoad(root);

                _root = root.transform;
            }

            var container = new GameObject("[Pool] " + id.Value);
            container.SetActive(false);
            container.transform.SetParent(_root, false);
            _containers[id] = container.transform;
            return container.transform;
        }

        private void DestroyContainer(PoolId id, Transform container)
        {
            if (_disposed || container == null || _pools.ContainsKey(id))
                return;

            if (_containers.TryGetValue(id, out var current) && ReferenceEquals(current, container))
                _containers.Remove(id);

            DestroyObject(container.gameObject);
        }

        private static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }
    }

    public sealed class PoolingInstaller : IFeatureInstaller
    {
        public void Install(IServiceRegistry registry)
        {
            // The two public constructors would be ambiguous to a container; the factory picks the injector-only one.
            registry.RegisterEntryPoint(
                resolver => new PoolService(resolver.Resolve<IGameObjectInjector>(), StopwatchPoolClock.Instance, resolver.TryResolve(out IAppLogger logger) ? logger : null),
                ServiceLifetime.Singleton).As<IPoolService>();
        }
    }
}
