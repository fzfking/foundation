using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Content
{
    public sealed class AssetHandleScopeTests
    {
        private readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var instance in _created)
                Object.DestroyImmediate(instance);
            _created.Clear();
        }

        [Test]
        public void Dispose_ReleasesEveryHandleOnce()
        {
            var provider = new FakeProvider();
            provider.Add("a", Create("a"));
            provider.Add("b", Create("b"));
            var scope = new AssetHandleScope(provider);
            scope.LoadAsync<GameObject>("a").GetAwaiter().GetResult();
            scope.LoadAsync<GameObject>("b").GetAwaiter().GetResult();

            scope.Dispose();
            scope.Dispose();

            Assert.That(provider.Released, Is.EqualTo(2));
        }

        [Test]
        public void Dispose_AttemptsEveryHandleAndRemainsIdempotentWhenOneReleaseThrows()
        {
            var provider = new FakeProvider();
            provider.Add("a", Create("a"));
            provider.Add("b", Create("b"));
            var scope = new AssetHandleScope(provider);
            scope.LoadAsync<GameObject>("a").GetAwaiter().GetResult();
            scope.LoadAsync<GameObject>("b").GetAwaiter().GetResult();
            provider.ThrowOnReleaseAddress = "a";

            Assert.Throws<AggregateException>(scope.Dispose);
            Assert.DoesNotThrow(scope.Dispose);

            Assert.That(scope.Count, Is.Zero);
            Assert.That(provider.Released, Is.EqualTo(2));
        }

        [Test]
        public void MissingAsset_ThrowsAndLeavesNothingHeld()
        {
            var provider = new FakeProvider();
            var scope = new AssetHandleScope(provider);

            Assert.Throws<InvalidOperationException>(() => scope.LoadAsync<GameObject>("missing").GetAwaiter().GetResult());

            Assert.That(scope.Count, Is.Zero);
            Assert.That(provider.Released, Is.EqualTo(provider.Loaded), "a handle without an asset is still released");
        }

        [Test]
        public void LoadComponent_RequiresTheComponentOnThePrefabRoot()
        {
            var provider = new FakeProvider();
            provider.Add("plain", Create("plain"));
            var withCollider = Create("collider");
            withCollider.AddComponent<BoxCollider>();
            provider.Add("collider", withCollider);
            using var scope = new AssetHandleScope(provider);

            Assert.Throws<InvalidOperationException>(
                () => scope.LoadComponentAsync<BoxCollider>("plain").GetAwaiter().GetResult());
            var component = scope.LoadComponentAsync<BoxCollider>("collider").GetAwaiter().GetResult();

            Assert.That(component.gameObject, Is.SameAs(withCollider));
        }

        [Test]
        public void LoadAfterDispose_IsRejected()
        {
            var provider = new FakeProvider();
            provider.Add("a", Create("a"));
            var scope = new AssetHandleScope(provider);
            scope.Dispose();

            Assert.Throws<ObjectDisposedException>(() => scope.LoadAsync<GameObject>("a").GetAwaiter().GetResult());
        }

        private GameObject Create(string name)
        {
            var instance = new GameObject(name);
            _created.Add(instance);
            return instance;
        }

        private sealed class FakeProvider : IAssetProvider
        {
            public bool ContainsAsset<T>(string assetId) where T : Object
                => throw new NotSupportedException();

            private readonly Dictionary<string, Object> _assets = new();

            public int Loaded { get; private set; }
            public int Released { get; private set; }
            public string ThrowOnReleaseAddress { get; set; }

            public void Add(string address, Object asset) => _assets[address] = asset;

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                Loaded++;
                _assets.TryGetValue(assetId, out var asset);
                return UniTask.FromResult<IAssetHandle<T>>(new Handle<T>(asset as T, () =>
                {
                    Released++;
                    if (assetId == ThrowOnReleaseAddress)
                        throw new InvalidOperationException("Injected release failure.");
                }));
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class Handle<T> : IAssetHandle<T> where T : Object
        {
            private readonly Action _release;
            private bool _disposed;

            public Handle(T asset, Action release)
            {
                Asset = asset;
                _release = release;
            }

            public T Asset { get; }

            public void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _release();
            }
        }
    }
}
