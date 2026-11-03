using System;
using System.Collections;
using System.Reflection;
using Object = UnityEngine.Object;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.Save;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Foundation.Tests.GameContexts
{
    public sealed class GameContextLifetimeTests
    {
        [Test]
        public void DisposingDuringStart_CancelsTheTransitionWithoutPublishingALateEntry()
        {
            var runtime = new Runtime();
            var factory = new Factory(runtime);
            var selection = new Selection();
            var definition = new GameContextDefinition(new GameContextId("main"),
                GameContextSaveProfile.Legacy("main", 1), new[] { new Installer() });
            var catalog = new GameContextCatalog(new[] { definition }, definition.Id);
            var controller = new GameContextController(catalog, factory, selection);
            var pending = controller.EnterAsync(definition.Id).Preserve();

            controller.Dispose();
            runtime.Ready.TrySetResult();

            Assert.Catch<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(factory.Scope.IsDisposed, Is.True);
            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(selection.Writes, Is.Zero);
            Assert.DoesNotThrow(controller.Dispose);
        }

        [Test]
        public void Dispose_WhenCancellationCallbackThrows_StillDisposesTheActiveScope()
        {
            var runtime = new Runtime { ThrowOnCancellation = true };
            var factory = new Factory(runtime);
            var definition = new GameContextDefinition(new GameContextId("main"),
                GameContextSaveProfile.Legacy("main", 1), new[] { new Installer() });
            var controller = new GameContextController(
                new GameContextCatalog(new[] { definition }, definition.Id), factory, new Selection());
            var pending = controller.EnterAsync(definition.Id).Preserve();

            Assert.Throws<AggregateException>(controller.Dispose);

            Assert.That(factory.Scope.IsDisposed, Is.True);
            Assert.That(controller.HasActiveContext, Is.False);
            runtime.Ready.TrySetResult();
            Assert.Catch<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
        }

        [UnityTest]
        public IEnumerator StopActiveAsync_StopsBeforeDisposing_WithoutSaving() => UniTask.ToCoroutine(async () =>
        {
            var runtime = new Runtime();
            var backend = new Backend();
            var factory = new Factory(runtime, Save(backend));
            var definition = new GameContextDefinition(new GameContextId("main"),
                GameContextSaveProfile.Legacy("main", 1), new[] { new Installer() });
            var controller = new GameContextController(
                new GameContextCatalog(new[] { definition }, definition.Id), factory, new Selection());
            runtime.Ready.TrySetResult();
            await controller.EnterAsync(definition.Id);

            await controller.StopActiveAsync();

            Assert.That(runtime.StopCalls, Is.EqualTo(1));
            Assert.That(runtime.ScopeWasDisposedWhenStopped, Is.False);
            Assert.That(factory.Scope.IsDisposed, Is.True);
            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(backend.Writes, Is.Zero);
        });

        [UnityTest]
        public IEnumerator DisposingWithAQueuedTransition_CancelsTheQueue() => UniTask.ToCoroutine(async () =>
        {
            var runtime = new Runtime();
            var factory = new Factory(runtime);
            var definition = new GameContextDefinition(new GameContextId("main"),
                GameContextSaveProfile.Legacy("main", 1), new[] { new Installer() });
            var controller = new GameContextController(
                new GameContextCatalog(new[] { definition }, definition.Id), factory, new Selection());
            var first = controller.EnterAsync(definition.Id).Preserve();
            var queued = controller.EnterAsync(definition.Id).Preserve();
            controller.Dispose();
            runtime.Ready.TrySetResult();

            Assert.That((await first.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That((await queued.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(controller.HasActiveContext, Is.False);
        });

        [UnityTest]
        public IEnumerator SavePump_ReinitializationRestoresPausePersistence() => UniTask.ToCoroutine(async () =>
        {
            var gameObject = new GameObject("save-pump-test");
            try
            {
                var pump = gameObject.AddComponent<SaveLifecyclePump>();
                var firstBackend = new Backend();
                var first = Save(firstBackend);
                pump.Initialize(first);
                await pump.DetachAsync(first);

                var secondBackend = new Backend();
                var second = Save(secondBackend);
                pump.Initialize(second);
                typeof(SaveLifecyclePump).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(pump, new object[] { true });
                Assert.That(secondBackend.Writes, Is.EqualTo(1));
                Assert.That(firstBackend.Writes, Is.Zero);

                await pump.DetachAsync(first);
                typeof(SaveLifecyclePump).GetMethod("OnApplicationPause", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(pump, new object[] { true });
                Assert.That(secondBackend.Writes, Is.EqualTo(2), "a stale detach cannot stop the new context");
                await pump.DetachAsync(second);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        });

        private static SaveOrchestrator Save(Backend backend)
            => new(Array.Empty<IFeatureSaveModule>(), backend, new Serializer());

        private sealed class Runtime : IGameContextRuntime
        {
            public readonly UniTaskCompletionSource Ready = new();
            public bool ThrowOnCancellation;
            public int StopCalls;
            public bool ScopeWasDisposedWhenStopped;
            public MinimalServiceScope Scope;

            public UniTask StartAsync(CancellationToken cancellationToken = default)
            {
                if (ThrowOnCancellation)
                    cancellationToken.Register(() => throw new InvalidOperationException("cancel callback failed"));

                return Ready.Task;
            }

            public UniTask StopAsync(CancellationToken cancellationToken = default)
            {
                StopCalls++;
                ScopeWasDisposedWhenStopped = Scope.IsDisposed;
                return UniTask.CompletedTask;
            }
        }

        private sealed class Factory : IGameContextScopeFactory
        {
            private readonly Runtime _runtime;
            private readonly SaveOrchestrator _save;
            public MinimalServiceScope Scope { get; private set; }

            public Factory(Runtime runtime, SaveOrchestrator save = null)
            {
                _runtime = runtime;
                _save = save;
            }

            public IServiceScope CreateScope(GameContextDefinition definition)
            {
                Scope = MinimalServiceScope.Create(registry =>
                {
                    registry.RegisterInstance<IGameContextRuntime>(_runtime);
                    if (_save != null)
                        registry.RegisterInstance(_save);
                });
                _runtime.Scope = Scope;
                return Scope;
            }
        }

        private sealed class Selection : IGameContextSelectionStore
        {
            public int Writes;
            public bool TryLoadLastActive(out GameContextId id)
            {
                id = default;
                return false;
            }
            public void SaveLastActive(GameContextId id) => Writes++;
            public void Clear() { }
        }

        private sealed class Installer : IFeatureInstaller
        {
            public void Install(IServiceRegistry registry) { }
        }

        private sealed class Backend : ISaveRepository
        {
            public int Writes;
            public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
                => UniTask.FromResult<SaveData>(null);
            public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
            {
                Writes++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class Serializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => throw new NotSupportedException();
            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();
            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }
    }
}
