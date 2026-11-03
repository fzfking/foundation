using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foundation.GameModules;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Foundation.DI;
using UnityEngine.TestTools;

namespace Foundation.Tests.GameContexts
{
    public sealed class GameModuleLoadingTests
    {
        [Test]
        public void ManifestLoader_LoadsDependenciesFirstAndDisposesInReverseOrder()
        {
            var order = new List<string>();
            var dependencyId = new GameModuleId("shared");
            var mainId = new GameModuleId("main");
            var dependencyHandle = new RecordingHandle(
                new RecordingModule(dependencyId, "shared", order), order);
            var mainHandle = new RecordingHandle(
                new RecordingModule(mainId, "main", order), order);
            var inner = new RecordingLoader(new Dictionary<GameModuleId, IGameModuleHandle>
            {
                [dependencyId] = dependencyHandle,
                [mainId] = mainHandle,
            }, order);
            var manifest = new GameModuleManifest(new[]
            {
                new GameModuleManifestEntry(dependencyId, "shared/assembly"),
                new GameModuleManifestEntry(mainId, "main/assembly", new[] { dependencyId }),
            });

            var loaded = Await(CreateLoader(inner, manifest).LoadAsync(mainId));
            loaded.Module.Configure(null);
            loaded.Dispose();

            Assert.That(order, Is.EqualTo(new[]
            {
                "load:shared",
                "load:main",
                "configure:shared",
                "configure:main",
                "dispose:main",
                "dispose:shared",
            }));
        }

        [Test]
        public void ManifestLoader_FailureLoadingRoot_ReleasesAlreadyLoadedDependencies()
        {
            var order = new List<string>();
            var dependencyId = new GameModuleId("shared");
            var mainId = new GameModuleId("main");
            var dependencyHandle = new RecordingHandle(
                new RecordingModule(dependencyId, "shared", order), order);
            var inner = new RecordingLoader(new Dictionary<GameModuleId, IGameModuleHandle>
            {
                [dependencyId] = dependencyHandle,
            }, order, mainId);
            var manifest = new GameModuleManifest(new[]
            {
                new GameModuleManifestEntry(dependencyId, "shared/assembly"),
                new GameModuleManifestEntry(mainId, "main/assembly", new[] { dependencyId }),
            });

            Assert.Throws<InvalidOperationException>(() =>
                Await(CreateLoader(inner, manifest).LoadAsync(mainId)));
            Assert.That(order, Is.EqualTo(new[] { "load:shared", "load:main", "dispose:shared" }));
        }

        [Test]
        public void ManifestLoader_LoadFailureAndCleanupFailure_PreservesBothErrors()
        {
            var order = new List<string>();
            var dependencyId = new GameModuleId("shared");
            var mainId = new GameModuleId("main");
            var dependencyHandle = new RecordingHandle(
                new RecordingModule(dependencyId, "shared", order), order,
                new InvalidOperationException("dispose shared failed"));
            var inner = new RecordingLoader(new Dictionary<GameModuleId, IGameModuleHandle>
            {
                [dependencyId] = dependencyHandle,
            }, order, mainId);
            var manifest = new GameModuleManifest(new[]
            {
                new GameModuleManifestEntry(dependencyId, "shared/assembly"),
                new GameModuleManifestEntry(mainId, "main/assembly", new[] { dependencyId }),
            });

            var failure = Assert.Throws<AggregateException>(() =>
                Await(CreateLoader(inner, manifest).LoadAsync(mainId)));

            var errors = failure.Flatten().InnerExceptions;
            Assert.That(errors.Any(exception => exception is InvalidOperationException), Is.True);
            Assert.That(errors.Any(exception => exception.Message == "dispose shared failed"), Is.True);
            Assert.That(order, Is.EqualTo(new[] { "load:shared", "load:main", "dispose:shared" }));
        }

        [Test]
        public void CompositeDispose_WhenOneHandleThrows_StillDisposesRemainingHandlesAndIsIdempotent()
        {
            var order = new List<string>();
            var firstId = new GameModuleId("first");
            var lastId = new GameModuleId("last");
            var first = new RecordingHandle(new RecordingModule(firstId, "first", order), order);
            var last = new RecordingHandle(new RecordingModule(lastId, "last", order), order,
                new InvalidOperationException("dispose last failed"));
            var composite = new CompositeGameModuleHandle(new GameModuleId("composite"), new[] { first, last });

            Assert.Throws<AggregateException>(composite.Dispose);
            Assert.DoesNotThrow(composite.Dispose);

            Assert.That(order, Is.EqualTo(new[] { "dispose:last", "dispose:first" }));
        }

        [Test]
        public void ModuleManifest_RejectsDependencyCyclesBeforeRuntimeLoading()
        {
            var firstId = new GameModuleId("first");
            var secondId = new GameModuleId("second");

            Assert.Throws<InvalidOperationException>(() => new GameModuleManifest(new[]
            {
                new GameModuleManifestEntry(firstId, "first/assembly", new[] { secondId }),
                new GameModuleManifestEntry(secondId, "second/assembly", new[] { firstId }),
            }));
        }

        [Test]
        public void ModuleManifest_ExposesValidatedEntriesAsReadOnly()
        {
            var entry = new GameModuleManifestEntry(new GameModuleId("main"), "main/assembly");
            var manifest = new GameModuleManifest(new[] { entry });
            var exposedEntries = (IList<GameModuleManifestEntry>)manifest.Entries;

            Assert.Throws<NotSupportedException>(() => exposedEntries.Add(
                new GameModuleManifestEntry(new GameModuleId("unlisted"), "other/assembly")));
            Assert.That(manifest.Entries, Has.Count.EqualTo(1));
            Assert.That(manifest.Get(new GameModuleId("main")), Is.SameAs(entry));
        }

        [Test]
        public void CachedLoader_LoadsPhysicalModuleOnceAndRetainsItUntilOwnerDisposes()
        {
            var order = new List<string>();
            var moduleId = new GameModuleId($"cache-once-{Guid.NewGuid():N}");
            var physicalHandle = new RecordingHandle(
                new RecordingModule(moduleId, "main", order), order);
            var inner = new RecordingLoader(new Dictionary<GameModuleId, IGameModuleHandle>
            {
                [moduleId] = physicalHandle,
            }, order);
            var cache = new CachedGameModuleLoader(inner);

            var firstLease = Await(cache.LoadAsync(moduleId));
            firstLease.Dispose();
            var secondLease = Await(cache.LoadAsync(moduleId));

            Assert.That(secondLease.Module, Is.SameAs(firstLease.Module));
            Assert.That(order, Is.EqualTo(new[] { $"load:{moduleId.Value}" }));

            secondLease.Dispose();
            cache.Dispose();
            cache.Dispose();

            Assert.That(order, Is.EqualTo(new[] { $"load:{moduleId.Value}", $"dispose:{moduleId.Value}" }));
        }

        [Test]
        public void CachedLoader_DisposeAttemptsEveryHandleWhenOneReleaseThrows()
        {
            var order = new List<string>();
            var firstId = new GameModuleId($"dispose-all-first-{Guid.NewGuid():N}");
            var secondId = new GameModuleId($"dispose-all-second-{Guid.NewGuid():N}");
            var inner = new RecordingLoader(new Dictionary<GameModuleId, IGameModuleHandle>
            {
                [firstId] = new RecordingHandle(new RecordingModule(firstId, "first", order), order),
                [secondId] = new RecordingHandle(new RecordingModule(secondId, "second", order), order,
                    new InvalidOperationException("dispose second failed")),
            }, order);
            var cache = new CachedGameModuleLoader(inner);
            Await(cache.LoadAsync(firstId));
            Await(cache.LoadAsync(secondId));

            Assert.Throws<AggregateException>(cache.Dispose);
            Assert.DoesNotThrow(cache.Dispose);

            Assert.That(order, Does.Contain($"dispose:{firstId.Value}"));
            Assert.That(order, Does.Contain($"dispose:{secondId.Value}"));
        }

        [Test]
        public void CachedLoader_RecreatedAfterRootScopeRestart_DoesNotLoadAssemblyAgain()
        {
            var order = new List<string>();
            var moduleId = new GameModuleId($"restart-safe-{Guid.NewGuid():N}");
            var physicalHandle = new RecordingHandle(
                new RecordingModule(moduleId, "restart-safe-module", order), order);
            var firstLoader = new CachedGameModuleLoader(new RecordingLoader(
                new Dictionary<GameModuleId, IGameModuleHandle>
                {
                    [moduleId] = physicalHandle,
                },
                order));

            var firstLease = Await(firstLoader.LoadAsync(moduleId));
            firstLoader.Dispose();

            var secondLoader = new CachedGameModuleLoader(new RecordingLoader(
                new Dictionary<GameModuleId, IGameModuleHandle>(),
                order,
                moduleId));
            var secondLease = Await(secondLoader.LoadAsync(moduleId));

            Assert.That(secondLease.Module, Is.SameAs(firstLease.Module));
            Assert.That(order, Is.EqualTo(new[] { $"load:{moduleId.Value}", $"dispose:{moduleId.Value}" }));

            secondLease.Dispose();
            secondLoader.Dispose();
        }

        [UnityTest]
        public IEnumerator CachedLoader_ConcurrentInstances_LoadPhysicalModuleOnce() => UniTask.ToCoroutine(async () =>
        {
            var order = new List<string>();
            var moduleId = new GameModuleId($"concurrent-{Guid.NewGuid():N}");
            var module = new RecordingModule(moduleId, "concurrent", order);
            var inner = new DeferredLoader();
            using var firstLoader = new CachedGameModuleLoader(inner);
            using var secondLoader = new CachedGameModuleLoader(inner);

            var first = firstLoader.LoadAsync(moduleId);
            var second = secondLoader.LoadAsync(moduleId);
            Assert.That(inner.LoadCount, Is.EqualTo(1));

            inner.Complete(new RecordingHandle(module, order));
            using var firstLease = await first;
            using var secondLease = await second;

            Assert.That(firstLease.Module, Is.SameAs(module));
            Assert.That(secondLease.Module, Is.SameAs(module));
            Assert.That(inner.LoadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator CachedLoader_CanceledWaitingInstance_DoesNotInterruptSharedLoad() => UniTask.ToCoroutine(async () =>
        {
            var order = new List<string>();
            var moduleId = new GameModuleId($"cancel-wait-{Guid.NewGuid():N}");
            var module = new RecordingModule(moduleId, "cancel-wait", order);
            var inner = new DeferredLoader();
            using var firstLoader = new CachedGameModuleLoader(inner);
            using var secondLoader = new CachedGameModuleLoader(inner);
            using var cancellationSource = new CancellationTokenSource();

            var first = firstLoader.LoadAsync(moduleId);
            var second = secondLoader.LoadAsync(moduleId, cancellationSource.Token);
            cancellationSource.Cancel();
            try
            {
                await second;
                Assert.Fail("The waiting load did not observe cancellation.");
            }
            catch (OperationCanceledException)
            {
            }

            inner.Complete(new RecordingHandle(module, order));
            using var firstLease = await first;
            using var secondLease = await secondLoader.LoadAsync(moduleId);
            Assert.That(firstLease.Module, Is.SameAs(module));
            Assert.That(secondLease.Module, Is.SameAs(module));
            Assert.That(inner.LoadCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator CachedLoader_DisposedDuringLoad_RetainsModuleForNextInstance() => UniTask.ToCoroutine(async () =>
        {
            var order = new List<string>();
            var moduleId = new GameModuleId($"disposed-during-load-{Guid.NewGuid():N}");
            var module = new RecordingModule(moduleId, "disposed", order);
            var inner = new DeferredLoader();
            var firstLoader = new CachedGameModuleLoader(inner);
            var first = firstLoader.LoadAsync(moduleId);

            firstLoader.Dispose();
            inner.Complete(new RecordingHandle(module, order));
            try
            {
                await first;
                Assert.Fail("The disposed loader returned a module.");
            }
            catch (ObjectDisposedException)
            {
            }

            using var secondLoader = new CachedGameModuleLoader(inner);
            using var lease = await secondLoader.LoadAsync(moduleId);
            Assert.That(lease.Module, Is.SameAs(module));
            Assert.That(inner.LoadCount, Is.EqualTo(1));
            Assert.That(order, Is.EqualTo(new[] { $"dispose:{moduleId.Value}" }));
        });

        private static T Await<T>(UniTask<T> task) => task.GetAwaiter().GetResult();

        private static ManifestGameModuleLoader CreateLoader(
            IGameModuleLoader inner,
            GameModuleManifest manifest)
            => new(new FixedManifestSource(manifest), _ => inner);

        private sealed class FixedManifestSource : IGameModuleManifestSource
        {
            public FixedManifestSource(GameModuleManifest manifest) => Current = manifest;

            public GameModuleManifest Current { get; }

            public UniTask EnsureRefreshedAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        private sealed class RecordingLoader : IGameModuleLoader
        {
            private readonly IReadOnlyDictionary<GameModuleId, IGameModuleHandle> _handles;
            private readonly List<string> _order;
            private readonly GameModuleId? _failId;

            public RecordingLoader(
                IReadOnlyDictionary<GameModuleId, IGameModuleHandle> handles,
                List<string> order,
                GameModuleId? failId = null)
            {
                _handles = handles;
                _order = order;
                _failId = failId;
            }

            public UniTask<IGameModuleHandle> LoadAsync(
                GameModuleId moduleId,
                CancellationToken cancellationToken = default)
            {
                _order.Add($"load:{moduleId.Value}");
                if (_failId.HasValue && _failId.Value == moduleId)
                    throw new InvalidOperationException("Injected module-load failure.");
                return UniTask.FromResult(_handles[moduleId]);
            }
        }

        private sealed class DeferredLoader : IGameModuleLoader
        {
            private readonly UniTaskCompletionSource<IGameModuleHandle> _completion = new();

            public int LoadCount { get; private set; }

            public UniTask<IGameModuleHandle> LoadAsync(
                GameModuleId moduleId,
                CancellationToken cancellationToken = default)
            {
                LoadCount++;
                return _completion.Task;
            }

            public void Complete(IGameModuleHandle handle) => _completion.TrySetResult(handle);
        }

        private sealed class RecordingModule : IGameModule
        {
            private readonly string _name;
            private readonly List<string> _order;

            public RecordingModule(GameModuleId id, string name, List<string> order)
            {
                Id = id;
                _name = name;
                _order = order;
            }

            public GameModuleId Id { get; }

            public void Configure(IServiceRegistry registry) => _order.Add($"configure:{_name}");
        }

        private sealed class RecordingHandle : IGameModuleHandle
        {
            private readonly List<string> _order;
            private readonly Exception _disposeFailure;

            public RecordingHandle(IGameModule module, List<string> order, Exception disposeFailure = null)
            {
                Module = module;
                _order = order;
                _disposeFailure = disposeFailure;
            }

            public IGameModule Module { get; }

            public void Dispose()
            {
                _order.Add($"dispose:{Module.Id.Value}");
                if (_disposeFailure != null)
                    throw _disposeFailure;
            }
        }
    }
}
