using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Foundation.Pooling;
using Foundation.Tests.Support;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Pooling
{
    public sealed class PrefabPoolTests
    {
        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _cleanup)
            {
                if (item != null)
                    Object.DestroyImmediate(item);
            }

            _cleanup.Clear();
        }

        [Test]
        public void IdleInstances_AreInactiveInTheHierarchy_AndActivateWhenRentedUnderTheRequestedParent()
        {
            var parent = Track(new GameObject("parent")).transform;
            using var pool = CreatePool<GameObject>(new PoolSettings());
            pool.Prewarm(1);

            var instance = pool.Rent(parent);

            Assert.That(instance.activeInHierarchy, Is.True);
            Assert.That(instance.transform.parent, Is.SameAs(parent));

            pool.Return(instance);
            Assert.That(instance.activeInHierarchy, Is.False);
            Assert.That(instance.transform.parent, Is.Not.SameAs(parent));
        }

        [Test]
        public void Rent_RestoresThePrefabTransform_SoAMovedInstanceDoesNotLeakItsPosition()
        {
            using var pool = CreatePool<GameObject>(new PoolSettings());
            var instance = pool.Rent();
            instance.transform.localPosition = new Vector3(5, 6, 7);
            instance.transform.localScale = Vector3.one * 3f;
            pool.Return(instance);

            var again = pool.Rent();

            Assert.That(again, Is.SameAs(instance));
            Assert.That(again.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(again.transform.localScale, Is.EqualTo(Vector3.one));
            pool.Return(again);
        }

        [Test]
        public void ComponentPool_ReturnsTheComponent_AndRunsPoolableHooksOnItsHierarchy()
        {
            using var pool = CreatePool<ProbeBehaviour>(new PoolSettings());

            var probe = pool.Rent();
            pool.Return(probe);
            pool.Return(pool.Rent());

            Assert.That(probe.Log, Is.EqualTo(new[] { "created", "rented", "returned", "rented", "returned" }));
            pool.Trim(0);
            Assert.That(probe == null, Is.True, "trimmed instances are destroyed");
        }

        [Test]
        public void ThrowingHook_DoesNotBreakThePool()
        {
            using var pool = CreatePool<ProbeBehaviour>(new PoolSettings());
            var probe = pool.Rent();
            probe.ThrowOnReturn = true;
            using var log = new LogCapture();

            pool.Return(probe);

            Assert.That(log.Contains(LogType.Error, "hook failed"), Is.True, "the hook failure is reported, not swallowed");
            Assert.That(pool.Stats.Inactive, Is.EqualTo(1));
            Assert.That(pool.Stats.Active, Is.EqualTo(0));
        }

        [Test]
        public void ActiveInstanceDestroyedByTheEngine_CanStillBeReturned_AndIsReplaced()
        {
            using var pool = CreatePool<GameObject>(new PoolSettings());
            var instance = pool.Rent();
            Object.DestroyImmediate(instance); // e.g. the scene it was parented to unloaded

            pool.Return(instance);
            var fresh = pool.Rent();

            Assert.That(pool.Stats.Active, Is.EqualTo(1));
            Assert.That(fresh != null, Is.True);
            pool.Return(fresh);
        }

        [Test]
        public void Dispose_DestroysInstancesBeforeReleasingThePrefabLease()
        {
            var order = new List<string>();
            var prefab = Track(new GameObject("prefab"));
            var lease = new RecordingLease(prefab, order);
            GameObject instance = null;
            lease.OnDispose = () => order.Add(instance == null ? "instance destroyed" : "instance alive");
            var pool = new PrefabPool<GameObject>(
                PoolId.From("lease"), prefab, new PoolSettings(), lease: lease,
                safetyChecks: true, violationHandler: ThrowingPoolViolationHandler.Instance);
            instance = pool.Rent();
            pool.Return(instance);

            pool.Dispose();

            Assert.That(order, Is.EqualTo(new[] { "instance destroyed" }),
                "the prefab asset may only be unloaded after every instance of it is gone");
            Assert.That(lease.Disposed, Is.True);
        }

        [UnityTest]
        public IEnumerator CreateAsync_UsesTheProvidersHandle_AndReleasesItWithThePool()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var prefab = Track(new GameObject("provided"));
                var assets = new FakeAssetProvider(prefab);
                var source = new AssetProviderPrefabSource(assets, "prefabs/provided");

                var pool = await PrefabPool<GameObject>.CreateAsync(
                    PoolId.From("provided"), source, new PoolSettings());
                var instance = pool.Rent();
                Assert.That(assets.RequestedIds, Is.EqualTo(new[] { "prefabs/provided" }));
                Assert.That(assets.HandleDisposed, Is.False, "the asset stays loaded while the pool lives");

                pool.Return(instance);
                pool.Dispose();

                Assert.That(assets.HandleDisposed, Is.True);
            });
        }

        [UnityTest]
        public IEnumerator CreateAsync_WithAMissingComponent_ReleasesTheLeaseAndFails()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var prefab = Track(new GameObject("plain"));
                var lease = new RecordingLease(prefab, new List<string>());

                try
                {
                    await PrefabPool<ProbeBehaviour>.CreateAsync(PoolId.From("missing"), new DelegateSource(lease));
                    Assert.Fail("A prefab without the requested component must not produce a pool.");
                }
                catch (InvalidOperationException)
                {
                }

                Assert.That(lease.Disposed, Is.True, "the loaded asset must not stay pinned by a failed pool");
            });
        }

        [UnityTest]
        public IEnumerator Service_FailedAsyncCreation_RemovesItsContainer()
        {
            return UniTask.ToCoroutine(async () =>
            {
                using var service = new PoolService(null);
                var id = PoolId.From("failed-async-create");
                try
                {
                    await service.CreatePrefabPoolAsync<GameObject>(id, new FailingSource());
                    Assert.Fail("The source should fail before a pool is created.");
                }
                catch (InvalidOperationException)
                {
                }

                await UniTask.Yield();
                foreach (var gameObject in Resources.FindObjectsOfTypeAll<GameObject>())
                    Assert.That(gameObject.name, Is.Not.EqualTo("[Pool] " + id.Value));
            });
        }

        [Test]
        public void Service_RejectsDuplicateIds_AndReleaseFreesTheIdAndTheContainer()
        {
            using var service = new PoolService(null);
            var prefab = Track(new GameObject("prefab"));
            var id = PoolId.From("bullets");
            service.CreatePrefabPool(id, prefab);

            Assert.Throws<InvalidOperationException>(() => service.CreatePrefabPool(id, prefab));
            Assert.That(service.TryGet(id, out _), Is.True);

            service.Release(id);

            Assert.That(service.TryGet(id, out _), Is.False);
            Assert.DoesNotThrow(() => service.CreatePrefabPool(id, prefab));
        }

        [UnityTest]
        public IEnumerator Service_PrewarmAll_ReachesEveryPoolsTarget_WithinTheFrameBudget()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var clock = new SteppingClock { CostPerRead = 1.0 };
                using var service = new PoolService(null, clock);
                var prefab = Track(new GameObject("prefab"));
                var a = service.CreatePrefabPool(PoolId.From("a"), prefab, new PoolSettings(prewarmCount: 4));
                var b = service.CreatePrefabPool(PoolId.From("b"), prefab, new PoolSettings(prewarmCount: 3));
                var progress = new List<float>();

                await service.PrewarmAllAsync(budgetMillisecondsPerFrame: 2500, new SyncProgress(progress));

                Assert.That(a.Stats.Inactive, Is.EqualTo(4));
                Assert.That(b.Stats.Inactive, Is.EqualTo(3));
                Assert.That(progress.Count, Is.GreaterThan(1), "7 instances cannot fit one 2.5 ms frame at 1 ms each");
                Assert.That(progress[progress.Count - 1], Is.EqualTo(1f));
            });
        }

        private PrefabPool<T> CreatePool<T>(PoolSettings settings)
            where T : Object
        {
            var prefab = Track(new GameObject("prefab"));
            prefab.AddComponent<ProbeBehaviour>();
            var typed = typeof(T) == typeof(GameObject) ? (T)(Object)prefab : (T)(Object)prefab.GetComponent<ProbeBehaviour>();
            return new PrefabPool<T>(
                PoolId.From("test." + typeof(T).Name), typed, settings,
                safetyChecks: true, violationHandler: ThrowingPoolViolationHandler.Instance);
        }

        private GameObject Track(GameObject gameObject)
        {
            _cleanup.Add(gameObject);
            return gameObject;
        }

        private sealed class SteppingClock : IPoolClock
        {
            private double _now;

            public double CostPerRead { get; set; }

            public double Now
            {
                get
                {
                    _now += CostPerRead;
                    return _now;
                }
            }
        }

        private sealed class SyncProgress : IProgress<float>
        {
            private readonly List<float> _reports;

            public SyncProgress(List<float> reports)
            {
                _reports = reports;
            }

            public void Report(float value) => _reports.Add(value);
        }

        private sealed class RecordingLease : IPrefabLease
        {
            private readonly List<string> _order;

            public RecordingLease(GameObject prefab, List<string> order)
            {
                Prefab = prefab;
                _order = order;
            }

            public GameObject Prefab { get; }

            public bool Disposed { get; private set; }

            public Action OnDispose { get; set; }

            public void Dispose()
            {
                Disposed = true;
                OnDispose?.Invoke();
            }
        }

        private sealed class DelegateSource : IPrefabSource
        {
            private readonly IPrefabLease _lease;

            public DelegateSource(IPrefabLease lease)
            {
                _lease = lease;
            }

            public UniTask<IPrefabLease> AcquireAsync(CancellationToken cancellationToken) => UniTask.FromResult(_lease);
        }

        private sealed class FailingSource : IPrefabSource
        {
            public UniTask<IPrefabLease> AcquireAsync(CancellationToken cancellationToken)
                => UniTask.FromException<IPrefabLease>(new InvalidOperationException("load failed"));
        }

        private sealed class FakeAssetProvider : IAssetProvider
        {
            public bool ContainsAsset<T>(string assetId) where T : Object
                => throw new NotSupportedException();

            private readonly GameObject _prefab;

            public FakeAssetProvider(GameObject prefab)
            {
                _prefab = prefab;
            }

            public List<string> RequestedIds { get; } = new();

            public bool HandleDisposed { get; private set; }

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                RequestedIds.Add(assetId);
                return UniTask.FromResult<IAssetHandle<T>>(new Handle<T>((T)(Object)_prefab, () => HandleDisposed = true));
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }

            private sealed class Handle<T> : IAssetHandle<T>
                where T : Object
            {
                private readonly Action _onDispose;

                public Handle(T asset, Action onDispose)
                {
                    Asset = asset;
                    _onDispose = onDispose;
                }

                public T Asset { get; }

                public void Dispose() => _onDispose();
            }
        }
    }

    public sealed class ProbeBehaviour : MonoBehaviour, IPoolable
    {
        public readonly List<string> Log = new();

        public bool ThrowOnReturn { get; set; }

        public void OnCreated() => Log.Add("created");

        public void OnRented() => Log.Add("rented");

        public void OnReturned()
        {
            Log.Add("returned");
            if (ThrowOnReturn)
                throw new InvalidOperationException("hook failed");
        }

        public void OnDestroying() => Log.Add("destroying");
    }
}
