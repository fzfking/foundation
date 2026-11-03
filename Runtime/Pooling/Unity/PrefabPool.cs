using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Logging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Pooling
{
    /// <summary>
    /// Pool of instances of a prefab. <typeparamref name="T"/> is <see cref="GameObject"/> or the component
    /// type the caller works with (a view, an effect), so gameplay never calls <c>GetComponent</c> after a
    /// rent. Idle instances live under an inactive container, so they get no Awake/OnEnable until first
    /// rented and no Update while idle. Renting parents and activates the instance; returning moves it back
    /// and deactivates it. <see cref="IPoolable"/> hooks are found once at creation; component state is reset
    /// by <see cref="IPoolable.OnReturned"/>, never by the pool.
    /// </summary>
    public sealed class PrefabPool<T> : IPool<T>
        where T : Object
    {
        private readonly Pool<T> _pool;
        private readonly PrefabStrategy _strategy;

        public PoolId Id => _pool.Id;
        public string Name => _pool.Name;
        public PoolStats Stats => _pool.Stats;
        public PoolSettings Settings => _pool.Settings;

        public PrefabPool(
            PoolId id,
            T prefab,
            PoolSettings settings = null,
            Transform container = null,
            IGameObjectInjector injector = null,
            IPrefabLease lease = null,
            string name = null,
            bool resetTransformOnRent = true,
            IPoolClock clock = null,
            bool? safetyChecks = null,
            IPoolViolationHandler violationHandler = null,
            bool createOnRent = true, IAppLogger logger = null)
        {
            if (prefab == null)
                throw new ArgumentNullException(nameof(prefab));

            var poolName = string.IsNullOrEmpty(name) ? (id.IsEmpty ? prefab.name : id.Value) : name;
            var ownsContainer = container == null;
            if (ownsContainer)
                container = CreateContainer(poolName);

            _strategy = new(prefab, container, ownsContainer, injector, lease, resetTransformOnRent,
                logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() }));
            _pool = new(id, _strategy, settings, poolName, clock, safetyChecks, violationHandler, createOnRent);
        }

        public void Dispose() => _pool.Dispose();

        /// <summary>
        /// Acquires the prefab from <paramref name="source"/>, then creates the pool. The pool owns the lease
        /// and releases it on <c>Dispose</c>, or immediately if creation fails.
        /// </summary>
        public static async UniTask<PrefabPool<T>> CreateAsync(
            PoolId id,
            IPrefabSource source,
            PoolSettings settings = null,
            Transform container = null,
            IGameObjectInjector injector = null,
            string name = null,
            IPoolClock clock = null,
            CancellationToken cancellationToken = default,
            IAppLogger logger = null)
        {
            var lease = await source.AcquireAsync(cancellationToken);
            try
            {
                var prefab = ResolvePrefab(lease.Prefab);
                return new PrefabPool<T>(id, prefab, settings, container, injector, lease, name, clock: clock, logger: logger);
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public T Rent() => Rent(null);

        /// <summary>Hands out an instance parented to <paramref name="parent"/> (null = scene root).</summary>
        public T Rent(Transform parent)
        {
            _strategy.PendingParent = parent;
            try
            {
                return _pool.Rent();
            }
            finally
            {
                _strategy.PendingParent = null;
            }
        }

        public bool TryRent(out T item) => TryRent(null, out item);

        public bool TryRent(Transform parent, out T item)
        {
            _strategy.PendingParent = parent;
            try
            {
                return _pool.TryRent(out item);
            }
            finally
            {
                _strategy.PendingParent = null;
            }
        }

        public void Return(T item) => _pool.Return(item);

        /// <summary>
        /// Takes an instance created outside the pool (through the shared spawner) as an idle member, so the
        /// pool never instantiates synchronously for it.
        /// </summary>
        public void Adopt(T item)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            _strategy.Register(item);
            _pool.Adopt(item);
        }

        public PoolLease<T> Lease() => new PoolLease<T>(this, Rent());

        public bool PrewarmStep(int targetInactive, double deadlineSeconds) => _pool.PrewarmStep(targetInactive, deadlineSeconds);

        public void Prewarm(int targetInactive) => _pool.Prewarm(targetInactive);

        public UniTask PrewarmAsync(
            int targetInactive,
            double budgetMillisecondsPerFrame,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            => _pool.PrewarmAsync(targetInactive, budgetMillisecondsPerFrame, progress, cancellationToken);

        public int Trim(int keep) => _pool.Trim(keep);

        public void Tick(double nowSeconds) => _pool.Tick(nowSeconds);

        private static T ResolvePrefab(GameObject prefab)
        {
            if (prefab == null)
                throw new InvalidOperationException("The prefab source returned no prefab.");

            if (typeof(T) == typeof(GameObject))
                return (T)(Object)prefab;

            var component = prefab.GetComponent(typeof(T));
            return component != null
                ? (T)(Object)component
                : throw new InvalidOperationException($"Prefab '{prefab.name}' has no {typeof(T).Name} component.");
        }

        private static Transform CreateContainer(string name)
        {
            var container = new GameObject("[Pool] " + name);
            container.SetActive(false);
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(container);

            return container.transform;
        }

        private static void DestroyObject(Object target)
        {
            if (Application.isPlaying)
                Object.Destroy(target);
            else
                Object.DestroyImmediate(target);
        }

        private sealed class PrefabStrategy : IPoolStrategy<T>, IDisposable
        {
            private readonly T _prefab;
            private readonly Transform _container;
            private readonly bool _ownsContainer;
            private readonly IGameObjectInjector _injector;
            private readonly IPrefabLease _lease;
            private readonly bool _resetTransform;
            private readonly IAppLogger _logger;
            private readonly Dictionary<T, InstanceData> _instances = new();

            public Transform PendingParent { get; set; }

            public PrefabStrategy(
                T prefab, Transform container, bool ownsContainer,
                IGameObjectInjector injector, IPrefabLease lease, bool resetTransform, IAppLogger logger)
            {
                _prefab = prefab;
                _container = container;
                _ownsContainer = ownsContainer;
                _injector = injector;
                _lease = lease;
                _resetTransform = resetTransform;
                _logger = logger;
            }

            public void Dispose()
            {
                _lease?.Dispose();
                if (_ownsContainer && _container != null)
                    DestroyObject(_container.gameObject);
            }

            public T Create()
            {
                var instance = Object.Instantiate(_prefab, _container);
                Register(instance);
                return instance;
            }

            // The bookkeeping Create() gives an instance the strategy made itself, for one that was made elsewhere.
            public void Register(T instance)
            {
                var gameObject = ToGameObject(instance);
                // Idle instances sit under an inactive container: self-active, but not active in the
                // hierarchy, so Awake/OnEnable wait until the first rent.
                if (!gameObject.activeSelf)
                    gameObject.SetActive(true);

                _injector?.Inject(gameObject);
                var data = new InstanceData(gameObject.transform, gameObject.GetComponentsInChildren<IPoolable>(true));
                _instances.Add(instance, data);
                foreach (var poolable in data.Poolables)
                    Invoke(poolable, HookKind.Created);
            }

            public bool IsValid(T item) => item != null;

            public void OnRent(T item)
            {
                var data = _instances[item];
                data.Transform.SetParent(PendingParent, false);
                if (_resetTransform)
                    data.RestoreTransform();

                foreach (var poolable in data.Poolables)
                    Invoke(poolable, HookKind.Rented);
            }

            public void OnReturn(T item)
            {
                var data = _instances[item];
                foreach (var poolable in data.Poolables)
                    Invoke(poolable, HookKind.Returned);

                data.Transform.SetParent(_container, false);
            }

            public void OnDestroy(T item)
            {
                if (!_instances.Remove(item, out var data))
                    return;

                if (item == null)
                    return;

                foreach (var poolable in data.Poolables)
                    Invoke(poolable, HookKind.Destroying);

                DestroyObject(ToGameObject(item));
            }

            private static GameObject ToGameObject(T item)
                => item is GameObject gameObject ? gameObject : ((Component)(object)item).gameObject;

            private void Invoke(IPoolable poolable, HookKind kind)
            {
                // A component destroyed with its instance must not break the pool; a throwing hook must not
                // leave the pool half-updated. Both are reported and the pool carries on.
                if (poolable is Object unityObject && unityObject == null)
                    return;

                try
                {
                    switch (kind)
                    {
                        case HookKind.Created: poolable.OnCreated(); break;
                        case HookKind.Rented: poolable.OnRented(); break;
                        case HookKind.Returned: poolable.OnReturned(); break;
                        default: poolable.OnDestroying(); break;
                    }
                }
                catch (Exception exception)
                {
                    _logger.Log(LogLevel.Error, "Pool", exception.ToString());
                }
            }
        }

        private enum HookKind
        {
            Created,
            Rented,
            Returned,
            Destroying,
        }

        private sealed class InstanceData
        {
            private readonly Vector3 _localPosition;
            private readonly Quaternion _localRotation;
            private readonly Vector3 _localScale;
            private readonly RectTransform _rect;
            private readonly Vector2 _anchorMin;
            private readonly Vector2 _anchorMax;
            private readonly Vector2 _pivot;
            private readonly Vector2 _sizeDelta;
            private readonly Vector3 _anchoredPosition3D;

            public Transform Transform { get; }

            public IPoolable[] Poolables { get; }

            public InstanceData(Transform transform, IPoolable[] poolables)
            {
                Transform = transform;
                Poolables = poolables;
                _localPosition = transform.localPosition;
                _localRotation = transform.localRotation;
                _localScale = transform.localScale;
                _rect = transform as RectTransform;
                if (_rect == null)
                    return;

                _anchorMin = _rect.anchorMin;
                _anchorMax = _rect.anchorMax;
                _pivot = _rect.pivot;
                _sizeDelta = _rect.sizeDelta;
                _anchoredPosition3D = _rect.anchoredPosition3D;
            }

            public void RestoreTransform()
            {
                if (_rect != null)
                {
                    _rect.anchorMin = _anchorMin;
                    _rect.anchorMax = _anchorMax;
                    _rect.pivot = _pivot;
                    _rect.sizeDelta = _sizeDelta;
                    _rect.anchoredPosition3D = _anchoredPosition3D;
                }
                else
                {
                    Transform.localPosition = _localPosition;
                }

                Transform.localRotation = _localRotation;
                Transform.localScale = _localScale;
            }
        }
    }
}
