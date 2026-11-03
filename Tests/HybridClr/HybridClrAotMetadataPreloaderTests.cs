using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.GameModules;
using Foundation.Infrastructure.HybridClr;
using NUnit.Framework;
using Object = UnityEngine.Object;
using UnityEngine;

namespace Foundation.Tests.HybridClr
{
    public sealed class HybridClrAotMetadataPreloaderTests
    {
        [Test]
        public void GameModuleLoader_RejectsDuplicateSourceIds()
        {
            var moduleId = new GameModuleId("duplicate");
            var sources = new[]
            {
                new HybridClrModuleSource(moduleId, "first"),
                new HybridClrModuleSource(moduleId, "second"),
            };

            Assert.Throws<ArgumentException>(() => new HybridClrGameModuleLoader(new FailingAssetProvider(), sources));
        }

        [Test]
        public void EnsureLoadedAsync_RetriesAfterAssetLoadingFails()
        {
            var provider = new FailingAssetProvider();
            var preloader = new HybridClrAotMetadataPreloader(provider, new[] { "aot/metadata" });

            Assert.Throws<InvalidOperationException>(() => preloader.EnsureLoadedAsync().GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => preloader.EnsureLoadedAsync().GetAwaiter().GetResult());

            Assert.That(provider.LoadCount, Is.EqualTo(2));
        }

        [Test]
        public void GameModuleLoader_PropagatesCancellationFromAssetProvider()
        {
            using var cancellationSource = new CancellationTokenSource();
            var loader = new HybridClrGameModuleLoader(
                new CanceledAssetProvider(cancellationSource),
                new[] { new HybridClrModuleSource(new GameModuleId("game"), "game/assembly") });

            Assert.Throws<OperationCanceledException>(() =>
                loader.LoadAsync(new GameModuleId("game"), cancellationSource.Token).GetAwaiter().GetResult());
        }

        [Test]
        public void GameModuleLoader_RejectsInvalidSignatureBeforeAssemblyLoad()
        {
            var provider = new FailingAssetProvider();
            var source = new RejectedAssemblySource();
            var loader = new HybridClrGameModuleLoader(
                provider,
                new[] { new HybridClrModuleSource(new GameModuleId("game"), "game/assembly") },
                assemblySource: source);

            var exception = Assert.Throws<GameModuleLoadException>(() =>
                loader.LoadAsync(new GameModuleId("game")).GetAwaiter().GetResult());

            Assert.That(exception.InnerException, Is.TypeOf<HotUpdateSignatureException>());
            Assert.That(source.LoadCount, Is.EqualTo(1));
            Assert.That(provider.LoadCount, Is.Zero);
        }

        [Test]
        public void AotPreloader_RejectsInvalidSignatureBeforeLoadingMetadata()
        {
            var provider = new FailingAssetProvider();
            var source = new RejectedAssemblySource();
            var preloader = new HybridClrAotMetadataPreloader(
                provider,
                new[] { "aot/metadata" },
                assemblySource: source);

            Assert.Throws<HotUpdateSignatureException>(() =>
                preloader.EnsureLoadedAsync().GetAwaiter().GetResult());

            Assert.That(source.LoadCount, Is.EqualTo(1));
            Assert.That(provider.LoadCount, Is.Zero);
        }

        [Test]
        public void GameModuleLoader_DisposesAssetHandleWhenCanceledAfterLoading()
        {
            using var cancellationSource = new CancellationTokenSource();
            var provider = new CancelAfterLoadAssetProvider(cancellationSource);
            var loader = new HybridClrGameModuleLoader(
                provider,
                new[] { new HybridClrModuleSource(new GameModuleId("game"), "game/assembly") });

            Assert.Throws<OperationCanceledException>(() =>
                loader.LoadAsync(new GameModuleId("game"), cancellationSource.Token).GetAwaiter().GetResult());
            Assert.That(provider.Handle.IsDisposed, Is.True);
        }

        private sealed class FailingAssetProvider : IAssetProvider
        {
            public int LoadCount { get; private set; }

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
                string assetId,
                CancellationToken cancellationToken = default)
                where T : Object
            {
                LoadCount++;
                return UniTask.FromException<IAssetHandle<T>>(new InvalidOperationException("Injected load failure."));
            }

            public UniTask<IAssetInstance> InstantiateAsync(
                string assetId,
                Transform parent = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public bool ContainsAsset<T>(string assetId)
                where T : Object
                => false;

            public UniTask<IPreloadHandle> PreloadAsync(
                IReadOnlyList<string> assetIds,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class RejectedAssemblySource : IHotUpdateAssemblySource
        {
            public int LoadCount { get; private set; }

            public UniTask<byte[]> LoadVerifiedAsync(string assemblyAddress, CancellationToken cancellationToken = default)
            {
                LoadCount++;
                return UniTask.FromException<byte[]>(new HotUpdateSignatureException("Injected invalid signature."));
            }
        }

        private sealed class CanceledAssetProvider : IAssetProvider
        {
            private readonly CancellationTokenSource _cancellationSource;

            public CanceledAssetProvider(CancellationTokenSource cancellationSource)
            {
                _cancellationSource = cancellationSource;
            }

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                _cancellationSource.Cancel();
                return UniTask.FromException<IAssetHandle<T>>(
                    new OperationCanceledException(_cancellationSource.Token));
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public bool ContainsAsset<T>(string assetId) where T : Object => false;

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class CancelAfterLoadAssetProvider : IAssetProvider
        {
            private readonly CancellationTokenSource _cancellationSource;

            public TestAssetHandle Handle { get; } = new TestAssetHandle();

            public CancelAfterLoadAssetProvider(CancellationTokenSource cancellationSource)
            {
                _cancellationSource = cancellationSource;
            }

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object
            {
                _cancellationSource.Cancel();
                return UniTask.FromResult((IAssetHandle<T>)(object)Handle);
            }

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public bool ContainsAsset<T>(string assetId) where T : Object => false;

            public UniTask<IPreloadHandle> PreloadAsync(IReadOnlyList<string> assetIds, IProgress<float> progress = null, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public void Dispose()
            {
            }
        }

        private sealed class TestAssetHandle : IAssetHandle<TextAsset>
        {
            public TextAsset Asset => null;

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }

}
