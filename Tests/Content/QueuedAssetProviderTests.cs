using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Content;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Content
{
    public sealed class QueuedAssetTestAsset : ScriptableObject
    {
    }

    [TestFixture]
    public sealed class QueuedAssetProviderTests
    {
        private FakeAssetProvider _inner;
        private QueuedAssetProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _inner = new();
            _provider = new(_inner);
        }

        [TearDown]
        public void TearDown()
        {
            _provider.Dispose();
            _inner.DestroyAssets();
        }

        [UnityTest]
        public IEnumerator ConcurrentRequestsForOneAddress_CauseOneRealLoad_AndShareOneAsset()
            => ConcurrentRequestsScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator DistinctRequests_NeverRunMoreThanTheInteractiveLaneAllows()
            => DistinctRequestsScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator FailedLoad_IsSharedByEveryWaiter_AndTheNextRequestLoadsAgain()
            => FailedLoadScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator AssetStaysLoaded_UntilTheLastHolderLetsGo()
            => ReleaseScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator TypedBackgroundPreload_ThenTypedRequest_GetsTheRequestedType()
            => TypedPreloadScenario().ToCoroutine();

        [UnityTest]
        public IEnumerator ProgressCallbackFailure_ReleasesEveryAssetLoadedByThePreload() => UniTask.ToCoroutine(async () =>
        {
            var assetIds = new[] { "preload-a", "preload-b", "preload-c" };
            Exception failure = null;
            try
            {
                await _provider.PreloadAsync(assetIds, new ThrowingProgress());
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
            Assert.That(failure.Message, Is.EqualTo("progress failed"));
            foreach (var assetId in assetIds)
                Assert.That(_inner.Released(assetId), Is.EqualTo(1), assetId);
        });

        [UnityTest]
        public IEnumerator CancellingAQueuedLoadOnAnotherThread_DoesNotCorruptTheLane()
            => UniTask.ToCoroutine(async () =>
            {
                using var provider = new QueuedAssetProvider(_inner, interactiveConcurrency: 1);
                var first = provider.LoadAssetAsync<QueuedAssetTestAsset>("lane-first");
                await UniTask.Delay(TimeSpan.FromMilliseconds(1), ignoreTimeScale: true);
                using var cancellation = new CancellationTokenSource();
                var second = provider.LoadAssetAsync<QueuedAssetTestAsset>("lane-cancelled", cancellation.Token);

                await Task.Run(cancellation.Cancel);

                var failure = await FailureOf(second);
                Assert.That(failure, Is.TypeOf<OperationCanceledException>());
                var firstHandle = await first;
                firstHandle.Dispose();
                var nextHandle = await provider.LoadAssetAsync<QueuedAssetTestAsset>("lane-next");
                nextHandle.Dispose();
                Assert.That(_inner.RealLoads("lane-next"), Is.EqualTo(1));
            });

        private async UniTask ConcurrentRequestsScenario()
        {
            var first = _provider.LoadAssetAsync<QueuedAssetTestAsset>("ship");
            var second = _provider.LoadAssetAsync<QueuedAssetTestAsset>("ship");
            var (firstHandle, secondHandle) = await UniTask.WhenAll(first, second);

            Assert.That(_inner.RealLoads("ship"), Is.EqualTo(1), "one address, requested twice in flight, must load once");
            Assert.That(secondHandle.Asset, Is.SameAs(firstHandle.Asset));
            firstHandle.Dispose();
            secondHandle.Dispose();
        }

        private async UniTask DistinctRequestsScenario()
        {
            var loads = new List<UniTask<IAssetHandle<QueuedAssetTestAsset>>>();
            for (var index = 0; index < 12; index++)
                loads.Add(_provider.LoadAssetAsync<QueuedAssetTestAsset>("sprite-" + index));

            var handles = await UniTask.WhenAll(loads);

            Assert.That(_inner.MaxInFlight, Is.LessThanOrEqualTo(QueuedAssetProvider.DefaultInteractiveConcurrency),
                "the interactive lane allows the default number of loads at once");
            Assert.That(_inner.MaxInFlight, Is.GreaterThan(1), "the lane must still run loads in parallel");
            foreach (var handle in handles)
                handle.Dispose();
        }

        private async UniTask FailedLoadScenario()
        {
            _inner.FailingAddress = "broken";
            var first = FailureOf(_provider.LoadAssetAsync<QueuedAssetTestAsset>("broken"));
            var second = FailureOf(_provider.LoadAssetAsync<QueuedAssetTestAsset>("broken"));
            var (firstError, secondError) = await UniTask.WhenAll(first, second);

            Assert.That(firstError, Is.Not.Null);
            Assert.That(secondError, Is.Not.Null, "every waiter gets the one failure");
            Assert.That(_inner.RealLoads("broken"), Is.EqualTo(1));

            _inner.FailingAddress = null;
            var retried = await _provider.LoadAssetAsync<QueuedAssetTestAsset>("broken");
            Assert.That(_inner.RealLoads("broken"), Is.EqualTo(2), "a request after a failure starts a new load");
            retried.Dispose();
        }

        private async UniTask ReleaseScenario()
        {
            var first = await _provider.LoadAssetAsync<QueuedAssetTestAsset>("hero");
            var second = await _provider.LoadAssetAsync<QueuedAssetTestAsset>("hero");
            Assert.That(_inner.RealLoads("hero"), Is.EqualTo(1));

            first.Dispose();
            Assert.That(_inner.Released("hero"), Is.EqualTo(0), "the second holder still uses it");

            second.Dispose();
            Assert.That(_inner.Released("hero"), Is.EqualTo(1), "the last holder releases the one real handle");
        }

        // Regression: a preload that asked for the plain object type left the wrong asset cached for the address, and the
        // later typed request failed. A typed preload must hand out the requested type.
        private async UniTask TypedPreloadScenario()
        {
            var preload = await _provider.PreloadInBackgroundAsync<QueuedAssetTestAsset>(new[] { "ship" });
            var handle = await _provider.LoadAssetAsync<QueuedAssetTestAsset>("ship");

            Assert.That(handle.Asset, Is.InstanceOf<QueuedAssetTestAsset>());
            handle.Dispose();
            preload.Dispose();
        }

        private static async UniTask<Exception> FailureOf(UniTask<IAssetHandle<QueuedAssetTestAsset>> load)
        {
            try
            {
                var handle = await load;
                handle.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        private sealed class FakeAssetProvider : IAssetProvider
        {
            private readonly Dictionary<string, Object> _assets = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _realLoads = new(StringComparer.Ordinal);
            private readonly Dictionary<string, int> _released = new(StringComparer.Ordinal);
            // Like the Addressables registry: the type an address was first loaded as sticks for that address.
            private readonly Dictionary<string, Type> _loadedAs = new(StringComparer.Ordinal);
            private int _inFlight;

            public int MaxInFlight { get; private set; }

            public string FailingAddress { get; set; }

            public int RealLoads(string address) => _realLoads.TryGetValue(address, out var count) ? count : 0;

            public int Released(string address) => _released.TryGetValue(address, out var count) ? count : 0;

            public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                _realLoads[assetId] = RealLoads(assetId) + 1;
                _inFlight++;
                MaxInFlight = Math.Max(MaxInFlight, _inFlight);
                try
                {
                    await UniTask.Delay(TimeSpan.FromMilliseconds(30), ignoreTimeScale: true, cancellationToken: cancellationToken);
                    if (assetId == FailingAddress)
                        throw new InvalidOperationException("load failed: " + assetId);
                    if (_loadedAs.TryGetValue(assetId, out var loadedAs) && loadedAs != typeof(T))
                        throw new InvalidOperationException($"Asset '{assetId}' is not of type {typeof(T).Name}. Actual type: {loadedAs.Name}.");

                    _loadedAs[assetId] = typeof(T);
                    var asset = (T)GetOrCreate(assetId);
                    return new FakeHandle<T>(asset, () => _released[assetId] = Released(assetId) + 1);
                }
                finally
                {
                    _inFlight--;
                }
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public bool ContainsAsset<T>(string assetId)
                where T : Object
                => false;

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }

            public void DestroyAssets()
            {
                foreach (var asset in _assets.Values)
                    Object.DestroyImmediate(asset);
                _assets.Clear();
            }

            private Object GetOrCreate(string assetId)
            {
                if (!_assets.TryGetValue(assetId, out var asset))
                {
                    asset = ScriptableObject.CreateInstance<QueuedAssetTestAsset>();
                    asset.name = assetId;
                    _assets[assetId] = asset;
                }

                return asset;
            }

            private sealed class FakeHandle<T> : IAssetHandle<T>
                where T : Object
            {
                private Action _release;

                public FakeHandle(T asset, Action release)
                {
                    Asset = asset;
                    _release = release;
                }

                public T Asset { get; }

                public void Dispose()
                {
                    var release = _release;
                    if (release == null)
                        return;

                    _release = null;
                    release();
                }
            }
        }

        private sealed class ThrowingProgress : IProgress<float>
        {
            public void Report(float value) => throw new InvalidOperationException("progress failed");
        }
    }
}
