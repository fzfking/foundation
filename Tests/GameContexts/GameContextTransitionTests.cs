using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.GameModules;
using Foundation.Messaging;
using Foundation.Save;
using Foundation.Tests.Support;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.GameContexts
{
    // A context switch preserves the outgoing context when save fails and attempts recovery when startup fails.
    public sealed class GameContextTransitionTests
    {
        private static readonly GameContextId Main = new("main");
        private static readonly GameContextId Broken = new("broken");
        private static readonly GameContextId Season = new("season");

        private MinimalServiceScope _application;

        [SetUp]
        public void SetUp() => _application = MinimalServiceScope.Create();

        [TearDown]
        public void TearDown() => _application.Dispose();

        [Test]
        public void GameContextCatalog_RejectsContextsThatShareAStorageSlot()
        {
            var sharedSlot = new GameContextSaveProfile("main", 1, slotId: "legacy");
            var otherSave = new GameContextSaveProfile("season", 1, slotId: "legacy");

            Assert.Throws<InvalidOperationException>(() => new GameContextCatalog(new[]
            {
                Definition(Main, new ContextBehaviour(new List<string>()), sharedSlot),
                Definition(Season, new ContextBehaviour(new List<string>()), otherSave),
            }, Main));
        }

        [Test]
        public void MaterializedCatalog_RejectsDuplicateSlotsWithoutPartiallyAddingDescriptor()
        {
            var catalog = new MaterializedGameContextCatalog(new[]
            {
                Descriptor(Main, save: new GameContextSaveProfile("main", 1, slotId: "legacy")),
            }, Main);
            var collision = Descriptor(Season, save: new GameContextSaveProfile("season", 1, slotId: "legacy"));

            Assert.That(catalog.TryAddDescriptor(collision), Is.False);
            Assert.That(catalog.Contains(Season), Is.False);
            Assert.That(catalog.TryAddDescriptor(Descriptor(Season)), Is.True);
        }

        [Test]
        public void MaterializedCatalog_RejectsInitialDescriptorsThatShareAStorageSlot()
        {
            Assert.Throws<InvalidOperationException>(() => new MaterializedGameContextCatalog(new[]
            {
                Descriptor(Main, save: new GameContextSaveProfile("main", 1, slotId: "legacy")),
                Descriptor(Season, save: new GameContextSaveProfile("season", 1, slotId: "legacy")),
            }, Main));
        }

        [Test]
        public void MaterializedCatalog_RejectsDefinitionThatChangesItsDescriptorSaveSlot()
        {
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main) }, Main);
            var mismatched = Definition(
                Main,
                new ContextBehaviour(new List<string>()),
                new GameContextSaveProfile("main", 1, slotId: "other"));

            Assert.Throws<InvalidOperationException>(() => catalog.Materialize(mismatched));
            Assert.That(catalog.TryGet(Main, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator AContextThatFailsToStart_HandsThePlayerBackToTheContextTheyCameFrom() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(starts)),
                Definition(Broken, new ContextBehaviour(starts) { FailStart = true, FailDispose = true }),
            });
            await controller.EnterAsync(Main);

            await ThrowsAsync<InvalidOperationException>(async () => await controller.EnterAsync(Broken));

            Assert.That(controller.HasActiveContext, Is.True);
            Assert.That(controller.ActiveContext.Id, Is.EqualTo(Main));
            Assert.That(controller.ActiveContext.State, Is.EqualTo(GameContextState.Active));
            Assert.That(starts, Is.EqualTo(new[] { "main", "broken", "main" }));
        });

        [UnityTest]
        public IEnumerator ASaveThatFails_KeepsTheOutgoingContextRunning_AndTheSwitchFails() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var failingSaves = new FailingSaveRepository();
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(starts) { Saves = failingSaves }),
                Definition(Season, new ContextBehaviour(starts)),
            });
            var main = await controller.EnterAsync(Main);

            await ThrowsAsync<InvalidOperationException>(async () => await controller.EnterAsync(Season));

            Assert.That(controller.ActiveContext, Is.SameAs(main), "the same context, never torn down");
            Assert.That(main.State, Is.EqualTo(GameContextState.Active));
            Assert.That(starts, Is.EqualTo(new[] { "main" }));
        });

        [UnityTest]
        public IEnumerator CallerCancellationAfterTeardown_DoesNotInterruptEntryIntoTheNextContext() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            using var cancellation = new CancellationTokenSource();
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(starts) { CancelCallerDuringStop = cancellation }),
                Definition(Season, new ContextBehaviour(starts)),
            });
            await controller.EnterAsync(Main);

            var entered = await controller.EnterAsync(Season, cancellation.Token);

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(entered.Id, Is.EqualTo(Season));
            Assert.That(controller.ActiveContext, Is.SameAs(entered));
            Assert.That(starts, Is.EqualTo(new[] { "main", "season" }));
        });

        [UnityTest]
        public IEnumerator ADisposeFailure_ClearsActiveContextAndPublishesExitBeforeFailingTheSwitch() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var events = new EventBus();
            var exited = new RecordingExitedHandler();
            events.Subscribe(exited);
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(starts) { FailDispose = true }),
                Definition(Season, new ContextBehaviour(starts)),
            }, events);
            await controller.EnterAsync(Main);

            await ThrowsAsync<AggregateException>(async () => await controller.EnterAsync(Season));

            Assert.That(controller.ActiveContext, Is.Null);
            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(exited.Count, Is.EqualTo(1));
            Assert.That(starts, Is.EqualTo(new[] { "main" }));
        });

        [UnityTest]
        public IEnumerator ControllerDispose_ClearsActiveContextEvenWhenScopeDisposeFails() => UniTask.ToCoroutine(async () =>
        {
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(new List<string>()) { FailDispose = true }),
            });
            await controller.EnterAsync(Main);

            Assert.Throws<AggregateException>(() => controller.Dispose());

            Assert.That(controller.ActiveContext, Is.Null);
            Assert.That(controller.HasActiveContext, Is.False);
        });

        [UnityTest]
        public IEnumerator AFailingEnteredSubscriber_DoesNotFailTheSwitch() => UniTask.ToCoroutine(async () =>
        {
            var events = new EventBus();
            events.Subscribe(new ThrowingEnteredHandler());
            var controller = CreateController(new[] { Definition(Main, new ContextBehaviour(new List<string>())) }, events);

            await controller.EnterAsync(Main);
            Assert.That(controller.ActiveContext.State, Is.EqualTo(GameContextState.Active));
        });

        [UnityTest]
        public IEnumerator AReset_IsStampedLaterThanASaveFromAClockThatRanAhead() => UniTask.ToCoroutine(async () =>
        {
            var saves = new InMemorySaveRepository();
            var ahead = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
            var profile = new GameContextSaveProfile("main", SaveOrchestrator.CurrentVersion);
            await saves.SaveAsync(
                new SaveData(SaveOrchestrator.CurrentVersion, new Dictionary<string, byte[]> { ["progress"] = new byte[] { 1 } }, ahead),
                default,
                profile.SlotId);
            var controller = CreateController(
                new[] { Definition(Main, new ContextBehaviour(new List<string>()) { Saves = saves }, profile) },
                saveRepository: saves);
            await controller.EnterAsync(Main);

            await controller.ResetActiveAsync();

            var wiped = await saves.LoadAsync(default, profile.SlotId);
            Assert.That(wiped.Segments, Is.Empty);
            // Backends keep the newest copy by this time: a lower stamp would let the pre-reset cloud copy come back.
            Assert.That(wiped.SavedAtUtcUnixMilliseconds, Is.GreaterThan(ahead));
        });

        [UnityTest]
        public IEnumerator ASelectionStoreFailure_DisposesTheFailedScopeAndRecoversThePreviousContext() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var selection = new ThrowingOnceSelectionStore();
            var controller = CreateController(new[]
            {
                Definition(Main, new ContextBehaviour(starts)),
                Definition(Broken, new ContextBehaviour(starts)),
            }, selectionStore: selection);
            await controller.EnterAsync(Main);

            await ThrowsAsync<InvalidOperationException>(async () => await controller.EnterAsync(Broken));

            Assert.That(controller.ActiveContext.Id, Is.EqualTo(Main));
            Assert.That(controller.ActiveContext.State, Is.EqualTo(GameContextState.Active));
            Assert.That(starts, Is.EqualTo(new[] { "main", "broken", "main" }));
            Assert.That(selection.SaveCount, Is.EqualTo(3));
        });

        [UnityTest]
        public IEnumerator ResetWithoutSaveRepository_FailsAndRestoresTheContext() => UniTask.ToCoroutine(async () =>
        {
            var controller = CreateController(new[] { Definition(Main, new ContextBehaviour(new List<string>())) });
            await controller.EnterAsync(Main);

            await ThrowsAsync<InvalidOperationException>(async () => await controller.ResetActiveAsync());

            Assert.That(controller.ActiveContext.Id, Is.EqualTo(Main));
            Assert.That(controller.ActiveContext.State, Is.EqualTo(GameContextState.Active));
        });

        [UnityTest]
        public IEnumerator AClosedSeason_IsNotEntered_TheFallbackIs() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var now = new DateTimeOffset(2027, 2, 1, 0, 0, 0, TimeSpan.Zero);
            var winter = new GameContextAvailability(
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var loader = new RecordingModuleLoader(starts);
            var controller = CreateModularController(loader, now, Descriptor(Main), Descriptor(Season, winter));

            var entered = await controller.EnterAsync(Season);

            Assert.That(entered.Id, Is.EqualTo(Main));
            Assert.That(starts, Is.EqualTo(new[] { "main" }));
        });

        [UnityTest]
        public IEnumerator ASeasonWhoseClockFails_CountsAsClosed_TheFallbackIsEntered() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var winter = new GameContextAvailability(
                new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main), Descriptor(Season, winter) }, Main);
            var controller = CreateModularController(new RecordingModuleLoader(starts), new FailingClock(), catalog);

            var entered = await controller.EnterAsync(Season);

            Assert.That(entered.Id, Is.EqualTo(Main));
        });

        [UnityTest]
        public IEnumerator AFailedSwitch_ReleasesOnlyTheModulesOfTheContextThatDidNotStart() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var loader = new RecordingModuleLoader(starts) { FailingStart = Broken };
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main), Descriptor(Broken) }, Main);
            var controller = CreateModularController(loader, DateTimeOffset.UtcNow, catalog);
            await controller.EnterAsync(Main);

            await ThrowsAsync<InvalidOperationException>(async () => await controller.EnterAsync(Broken));

            Assert.That(controller.ActiveContext.Id, Is.EqualTo(Main));
            Assert.That(loader.Released, Is.EqualTo(new[] { "broken" }), "the running context keeps its modules");
            Assert.That(catalog.TryGet(Broken, out _), Is.False);
            Assert.That(catalog.TryGet(Main, out _), Is.True);
        });

        [UnityTest]
        public IEnumerator DisposingModularController_ClosesTheContextBeforeReleasingItsModules() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            ModularGameContextController controller = null;
            var loader = new RecordingModuleLoader(starts)
            {
                OnRelease = () => Assert.That(controller.HasActiveContext, Is.False),
            };
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main) }, Main);
            controller = CreateModularController(loader, DateTimeOffset.UtcNow, catalog);
            await controller.EnterAsync(Main);

            controller.Dispose();

            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(loader.Released, Is.EqualTo(new[] { "main" }));
            Assert.That(catalog.TryGet(Main, out _), Is.False);
            Assert.DoesNotThrow(controller.Dispose);
        });

        [UnityTest]
        public IEnumerator DisposingDuringBeforeSwitch_CannotEnterLate_AndReleasesLoadedModulesOnce() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var loader = new RecordingModuleLoader(starts);
            var hooks = new TestTransitionHooks { BlockBeforeSwitch = true };
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main) }, Main);
            var controller = CreateModularController(loader, new FixedClock(DateTimeOffset.UtcNow), catalog, hooks);
            var pending = controller.EnterAsync(Main).Preserve();
            await hooks.BeforeSwitchEntered.Task;

            controller.Dispose();
            hooks.ContinueBeforeSwitch.TrySetResult();

            Assert.Catch<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(hooks.EnteredCalls, Is.Zero);
            Assert.That(loader.Released, Is.EqualTo(new[] { "main" }));
            Assert.That(catalog.TryGet(Main, out _), Is.False);
        });

        [UnityTest]
        public IEnumerator AThrowingExitedHook_DoesNotSkipInnerOrModuleCleanup() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var loader = new RecordingModuleLoader(starts);
            var hooks = new TestTransitionHooks { ThrowOnExited = true };
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main) }, Main);
            var controller = CreateModularController(loader, new FixedClock(DateTimeOffset.UtcNow), catalog, hooks);
            await controller.EnterAsync(Main);

            Assert.Throws<InvalidOperationException>(controller.Dispose);

            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(loader.Released, Is.EqualTo(new[] { "main" }));
            Assert.That(catalog.TryGet(Main, out _), Is.False);
            Assert.DoesNotThrow(controller.Dispose);
        });

        [UnityTest]
        public IEnumerator StopActiveAsync_StopsAndReleasesModulesWithoutReentering() => UniTask.ToCoroutine(async () =>
        {
            var starts = new List<string>();
            var loader = new RecordingModuleLoader(starts);
            var catalog = new MaterializedGameContextCatalog(new[] { Descriptor(Main) }, Main);
            var controller = CreateModularController(loader, DateTimeOffset.UtcNow, catalog);
            await controller.EnterAsync(Main);

            await controller.StopActiveAsync();

            Assert.That(controller.HasActiveContext, Is.False);
            Assert.That(starts, Is.EqualTo(new[] { "main" }));
            Assert.That(loader.Released, Is.EqualTo(new[] { "main" }));
            Assert.That(catalog.TryGet(Main, out _), Is.False);
        });

        private GameContextController CreateController(
            IReadOnlyList<GameContextDefinition> definitions,
            IEventBus events = null,
            ISaveRepository saveRepository = null,
            IGameContextSelectionStore selectionStore = null)
            => new(
                new GameContextCatalog(definitions, definitions[0].Id),
                new TestGameContextScopeFactory(_application),
                selectionStore ?? new MemorySelectionStore(),
                saveRepository,
                events);

        private ModularGameContextController CreateModularController(
            IGameModuleLoader loader,
            DateTimeOffset now,
            params GameContextDescriptor[] descriptors)
            => CreateModularController(loader, now, new MaterializedGameContextCatalog(descriptors, Main));

        private ModularGameContextController CreateModularController(
            IGameModuleLoader loader,
            DateTimeOffset now,
            MaterializedGameContextCatalog catalog)
            => CreateModularController(loader, new FixedClock(now), catalog);

        private ModularGameContextController CreateModularController(
            IGameModuleLoader loader,
            IGameContextClock clock,
            MaterializedGameContextCatalog catalog,
            IGameContextTransitionHooks hooks = null)
        {
            var inner = new GameContextController(
                catalog,
                new TestGameContextScopeFactory(_application),
                new MemorySelectionStore());
            return new ModularGameContextController(inner, catalog, loader, hooks: hooks, clock: clock);
        }

        private sealed class TestTransitionHooks : IGameContextTransitionHooks
        {
            public readonly UniTaskCompletionSource BeforeSwitchEntered = new();
            public readonly UniTaskCompletionSource ContinueBeforeSwitch = new();

            public bool BlockBeforeSwitch { get; set; }

            public bool ThrowOnExited { get; set; }

            public int EnteredCalls { get; private set; }

            public UniTask PrepareAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public async UniTask BeforeSwitchAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken)
            {
                if (!BlockBeforeSwitch)
                    return;

                BeforeSwitchEntered.TrySetResult();
                await ContinueBeforeSwitch.Task;
            }

            public void Entered(GameContext context) => EnteredCalls++;

            public UniTask SwitchFailedAsync(GameContext active) => UniTask.CompletedTask;

            public void Exited()
            {
                if (ThrowOnExited)
                    throw new InvalidOperationException("Exited hook failed");
            }
        }

        private sealed class FixedClock : IGameContextClock
        {
            private readonly DateTimeOffset _now;

            public FixedClock(DateTimeOffset now) => _now = now;

            public UniTask<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default)
                => UniTask.FromResult(_now);
        }

        private sealed class FailingClock : IGameContextClock
        {
            public UniTask<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("server time unavailable");
        }

        private static GameContextDefinition Definition(
            GameContextId id,
            ContextBehaviour behaviour,
            GameContextSaveProfile save = null)
            => new(id, save ?? new GameContextSaveProfile(id.Value, 1), new IFeatureInstaller[] { new ContextInstaller(id.Value, behaviour) });

        private static GameContextDescriptor Descriptor(
            GameContextId id,
            GameContextAvailability availability = default,
            GameContextSaveProfile save = null)
            => new(id, save ?? new GameContextSaveProfile(id.Value, 1), new[] { new GameModuleId(id.Value) }, availability: availability);

        private static async UniTask ThrowsAsync<TException>(Func<UniTask> call) where TException : Exception
        {
            try
            {
                await call();
            }
            catch (TException)
            {
                return;
            }

            Assert.Fail($"Expected {typeof(TException).Name}.");
        }

        private sealed class ContextBehaviour
        {
            public ContextBehaviour(List<string> starts) => Starts = starts;

            public List<string> Starts { get; }

            public bool FailStart { get; set; }

            public bool FailDispose { get; set; }

            public CancellationTokenSource CancelCallerDuringStop { get; set; }

            public ISaveRepository Saves { get; set; }
        }

        private sealed class ContextInstaller : IFeatureInstaller, IGameModule
        {
            private readonly string _name;
            private readonly ContextBehaviour _behaviour;

            public ContextInstaller(string name, ContextBehaviour behaviour)
            {
                _name = name;
                _behaviour = behaviour;
            }

            public GameModuleId Id => new(_name);

            public void Install(IServiceRegistry builder) => Configure(builder);

            public void Configure(IServiceRegistry builder)
            {
                if (_behaviour.Saves != null)
                {
                    builder.Register(_ => new SaveOrchestrator(
                        Array.Empty<IFeatureSaveModule>(), _behaviour.Saves, new UnusedSerializer()), ServiceLifetime.Singleton);
                }

                builder.Register<IGameContextRuntime>(resolver => new ContextRuntime(
                        _name,
                        _behaviour,
                        resolver.Resolve<GameContextSaveProfile>(),
                        resolver.TryResolve<SaveOrchestrator>(out var save) ? save : null),
                    ServiceLifetime.Singleton);
            }
        }

        private sealed class ContextRuntime : IGameContextRuntime, IDisposable
        {
            private readonly string _name;
            private readonly ContextBehaviour _behaviour;
            private readonly GameContextSaveProfile _profile;
            private readonly SaveOrchestrator _save;

            public ContextRuntime(string name, ContextBehaviour behaviour, GameContextSaveProfile profile, SaveOrchestrator save)
            {
                _name = name;
                _behaviour = behaviour;
                _profile = profile;
                _save = save;
            }

            public async UniTask StartAsync(CancellationToken cancellationToken = default)
            {
                _behaviour.Starts.Add(_name);
                if (_behaviour.FailStart)
                    throw new InvalidOperationException($"'{_name}' cannot start.");
                if (_save != null)
                    await _save.LoadAsync(cancellationToken, _profile.SlotId);
            }

            public UniTask StopAsync(CancellationToken cancellationToken = default)
            {
                _behaviour.CancelCallerDuringStop?.Cancel();
                return UniTask.CompletedTask;
            }

            public void Dispose()
            {
                if (_behaviour.FailDispose)
                    throw new InvalidOperationException("context dispose failed");
            }
        }

        private sealed class RecordingModuleLoader : IGameModuleLoader
        {
            private readonly List<string> _starts;

            public RecordingModuleLoader(List<string> starts) => _starts = starts;

            public Action OnRelease { get; set; }

            public GameContextId FailingStart { get; set; }

            public List<string> Released { get; } = new();

            public UniTask<IGameModuleHandle> LoadAsync(GameModuleId moduleId, CancellationToken cancellationToken = default)
            {
                var behaviour = new ContextBehaviour(_starts) { FailStart = moduleId.Value == FailingStart.Value };
                return UniTask.FromResult<IGameModuleHandle>(
                    new RecordingHandle(new ContextInstaller(moduleId.Value, behaviour), Released, OnRelease));
            }
        }

        private sealed class RecordingHandle : IGameModuleHandle
        {
            private readonly List<string> _released;
            private readonly Action _onRelease;

            public RecordingHandle(IGameModule module, List<string> released, Action onRelease)
            {
                Module = module;
                _released = released;
                _onRelease = onRelease;
            }

            public IGameModule Module { get; }

            public void Dispose()
            {
                _released.Add(Module.Id.Value);
                _onRelease?.Invoke();
            }
        }

        private sealed class FailingSaveRepository : ISaveRepository
        {
            public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
                => UniTask.FromResult<SaveData>(null);

            public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
                => throw new InvalidOperationException("The disk is full.");
        }

        private sealed class ThrowingEnteredHandler : IEventHandler<GameContextEnteredEvent>
        {
            public UniTask HandleAsync(GameContextEnteredEvent message, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("A subscriber broke.");
        }

        private sealed class RecordingExitedHandler : IEventHandler<GameContextExitedEvent>
        {
            public int Count { get; private set; }

            public UniTask HandleAsync(GameContextExitedEvent message, CancellationToken cancellationToken = default)
            {
                Count++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class MemorySelectionStore : IGameContextSelectionStore
        {
            private GameContextId _last;

            public bool TryLoadLastActive(out GameContextId contextId)
            {
                contextId = _last;
                return !_last.IsEmpty;
            }

            public void SaveLastActive(GameContextId contextId) => _last = contextId;

            public void Clear() => _last = default;
        }

        private sealed class ThrowingOnceSelectionStore : IGameContextSelectionStore
        {
            private readonly MemorySelectionStore _inner = new();
            public int SaveCount { get; private set; }

            public bool TryLoadLastActive(out GameContextId contextId) => _inner.TryLoadLastActive(out contextId);

            public void SaveLastActive(GameContextId contextId)
            {
                SaveCount++;
                if (SaveCount == 2)
                    throw new InvalidOperationException("selection store failed");
                _inner.SaveLastActive(contextId);
            }

            public void Clear() => _inner.Clear();
        }

        private sealed class UnusedSerializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => throw new NotSupportedException();
            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();
            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }
    }
}
