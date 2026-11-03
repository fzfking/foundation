using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Config;
using Foundation.Content;
using Foundation.DI;
using Foundation.Serialization;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Config
{
    // Local fixture types stand in for a game's concrete config schema: this package must not
    // reference Foundation.Content.Data.
    public sealed class TestConfig
    {
        public string Id;
    }

    public sealed class TestOtherConfig
    {
        public string Id;
    }

    public class SerializedConfigProviderTests
    {
        private ConfigGroupRegistry _groups;
        private ConfigRepository _repository;
        private FakeAssetProvider _assets;
        private FakeSerializer _serializer;
        private SerializedConfigProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _groups = new();
            _repository = new();
            _assets = new FakeAssetProvider();
            _serializer = new();
            _provider = new(_assets, _serializer, _repository, _groups);

            _groups.Register<TestConfig>("planets", "config/planets", p => p.Id);
        }

        [Test]
        public void LoadGroup_RegistersConfigs()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[]
            {
                new TestConfig { Id = "mars" },
                new TestConfig { Id = "venus" },
            };

            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();

            Assert.That(_repository.Get<TestConfig>("mars").Id, Is.EqualTo("mars"));
            Assert.That(_repository.Get<TestConfig>("venus").Id, Is.EqualTo("venus"));
            Assert.That(_repository.GetAll<TestConfig>(), Has.Count.EqualTo(2));
        }

        [Test]
        public void ConcurrentGroupLoad_LoadsOnlyOnce()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _assets.Gate = new UniTaskCompletionSource();
            _serializer.OnDeserialize = _ => new[]
            {
                new TestConfig { Id = "mars" },
            };

            _provider.EnsureAsync<TestConfig>();
            _provider.EnsureAsync<TestConfig>();

            Assert.That(_assets.LoadCount, Is.EqualTo(1));

            _assets.Gate.TrySetResult();
        }

        [Test]
        public void RepeatedEnsure_DoesNotReload()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };

            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();
            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();
            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();

            Assert.That(_assets.LoadCount, Is.EqualTo(1));
        }

        [Test]
        public void FailedLoad_ClearsPending_AndAllowsRetry()
        {
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };

            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            _assets.Add("config/planets", new TextAsset("x"));

            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();

            Assert.That(_repository.Contains<TestConfig>("mars"), Is.True);
        }

        [Test]
        public void WaiterCancellation_DoesNotCancelSharedLoad()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _assets.Gate = new UniTaskCompletionSource();
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };

            var ctsA = new CancellationTokenSource();
            var taskA = _provider.EnsureAsync<TestConfig>(ctsA.Token);
            var taskB = _provider.EnsureAsync<TestConfig>(CancellationToken.None);

            ctsA.Cancel();

            Assert.That(taskA.Status, Is.EqualTo(UniTaskStatus.Canceled));
            Assert.That(taskB.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(_assets.LoadCount, Is.EqualTo(1));

            _assets.Gate.TrySetResult();
        }

        [Test]
        public void PreCancelledWaiter_DoesNotBlockSharedLoad()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };

            var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => _provider.EnsureAsync<TestConfig>(cts.Token).GetAwaiter().GetResult());

            Assert.That(_repository.Contains<TestConfig>("mars"), Is.True);
            Assert.That(_assets.LoadCount, Is.EqualTo(1));
        }

        [Test]
        public void Shutdown_CancelsSharedLoad_AndPreventsLateRegistration()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _assets.Gate = new UniTaskCompletionSource();
            _assets.IgnoreCancellation = true;
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };
            var pending = _provider.EnsureAsync<TestConfig>();

            _provider.Shutdown();
            Assert.That(_assets.LastCancellationToken.IsCancellationRequested, Is.True);
            _assets.Gate.TrySetResult();

            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(_repository.GetAll<TestConfig>(), Is.Empty);
            Assert.That(_assets.ReleasedIds, Is.EqualTo(new[] { "config/planets" }));

            Assert.Throws<OperationCanceledException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());
            Assert.That(_assets.LoadCount, Is.EqualTo(1));
            Assert.That(_repository.GetAll<TestConfig>(), Is.Empty);
        }

        [Test]
        public void ConfigScopeDisposal_ShutsDownProviderAndRejectsLateLoad()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _assets.Gate = new UniTaskCompletionSource();
            _assets.IgnoreCancellation = true;
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };
            var scope = MinimalServiceScope.Create(registry =>
            {
                registry.RegisterInstance<IAssetProvider>(_assets);
                registry.RegisterInstance<ISerializer>(_serializer);
                registry.UseConfigs();
                registry.RegisterBuildCallback(resolver =>
                    resolver.Resolve<IConfigGroupRegistry>().Register<TestConfig>("planets", "config/planets", config => config.Id));
            });
            var provider = scope.Resolve<IConfigProvider>();
            var repository = scope.Resolve<IConfigRepository>();
            var pending = provider.EnsureAsync<TestConfig>();

            scope.Dispose();
            Assert.That(_assets.LastCancellationToken.IsCancellationRequested, Is.True);
            _assets.Gate.TrySetResult();

            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(repository.GetAll<TestConfig>(), Is.Empty);
            Assert.That(_assets.ReleasedIds, Is.EqualTo(new[] { "config/planets" }));
        }

        [Test]
        public void DeserializeError_ContainsGroupIdAndExpectedType()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => throw new InvalidOperationException("boom");

            var ex = Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain("planets"));
            Assert.That(ex.Message, Does.Contain("TestConfig"));
        }

        [Test]
        public void SuccessfulLoad_ReleasesAssetHandle()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "mars" } };

            _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult();

            Assert.That(_assets.ReleasedIds, Has.Count.EqualTo(1));
            Assert.That(_assets.ReleasedIds[0], Is.EqualTo("config/planets"));
        }

        [Test]
        public void FailedDeserialize_ReleasesHandle_AndRegistersNothing()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => throw new InvalidOperationException("deserialize failed");

            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            Assert.That(_assets.ReleasedIds, Has.Count.EqualTo(1));
            Assert.That(_repository.GetAll<TestConfig>(), Is.Empty);
        }

        [Test]
        public void DuplicateId_PreventsPartialRegistration()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[]
            {
                new TestConfig { Id = "mars" },
                new TestConfig { Id = "mars" },
            };

            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            Assert.That(_repository.GetAll<TestConfig>(), Is.Empty);
        }

        [Test]
        public void ExistingId_PreventsAnyRegistrationFromTheIncomingGroup()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _repository.Register("venus", new TestConfig { Id = "existing" });
            _serializer.OnDeserialize = _ => new[]
            {
                new TestConfig { Id = "mars" },
                new TestConfig { Id = "venus" },
            };

            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            Assert.That(_repository.Contains<TestConfig>("mars"), Is.False);
            Assert.That(_repository.Get<TestConfig>("venus").Id, Is.EqualTo("existing"));
        }

        [Test]
        public void EmptyId_PreventsRegistration()
        {
            _assets.Add("config/planets", new TextAsset("x"));
            _serializer.OnDeserialize = _ => new[] { new TestConfig { Id = "" } };

            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestConfig>().GetAwaiter().GetResult());

            Assert.That(_repository.GetAll<TestConfig>(), Is.Empty);
        }

        [Test]
        public void UnregisteredType_Throws()
        {
            Assert.Throws<InvalidOperationException>(
                () => _provider.EnsureAsync<TestOtherConfig>());
        }

        private sealed class FakeAssetProvider : IAssetProvider
        {
            public bool ContainsAsset<T>(string assetId) where T : Object
                => throw new NotSupportedException();

            private readonly Dictionary<string, TextAsset> _assets = new();

            public UniTaskCompletionSource Gate;
            public bool IgnoreCancellation;
            public int LoadCount;
            public CancellationToken LastCancellationToken;
            public readonly List<string> ReleasedIds = new();

            public void Add(string assetId, TextAsset asset)
            {
                _assets[assetId] = asset;
            }

            public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
                string assetId,
                CancellationToken cancellationToken = default)
                where T : Object
            {
                LoadCount++;
                LastCancellationToken = cancellationToken;

                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException(cancellationToken);

                if (Gate != null)
                {
                    if (IgnoreCancellation)
                        await Gate.Task;
                    else
                        await Gate.Task.AttachExternalCancellation(cancellationToken);
                }

                if (!_assets.TryGetValue(assetId, out var asset))
                    throw new InvalidOperationException($"Missing asset '{assetId}'.");

                return new FakeAssetHandle<T>((T)(object)asset, () => ReleasedIds.Add(assetId));
            }

            public UniTask<IPreloadHandle> PreloadAsync(
                IReadOnlyList<string> assetIds,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public UniTask<IAssetInstance> InstantiateAsync(
                string assetId,
                Transform parent = null,
                CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException();
            }

            public void Dispose()
            {
            }
        }

        private sealed class FakeAssetHandle<T> : IAssetHandle<T>
            where T : Object
        {
            private readonly Action _release;

            public T Asset { get; }

            public FakeAssetHandle(T asset, Action release)
            {
                Asset = asset;
                _release = release;
            }

            public void Dispose()
            {
                _release?.Invoke();
            }
        }

        private sealed class FakeSerializer : ISerializer
        {
            public Func<Type, object> OnDeserialize;

            public byte[] Serialize<T>(T value) => throw new NotSupportedException();

            public T Deserialize<T>(byte[] bytes) => (T)OnDeserialize(typeof(T));

            public object Deserialize(Type type, byte[] bytes) => OnDeserialize(type);
        }
    }
}
