using System;
using System.Collections;
using System.Collections.Generic;
using Foundation.StateMachine;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests
{
    public interface IAppState : IState { }
    public interface IGameState : IState { }

    public class FakeAppStateA : IAppState
    {
        public int EnterCount;
        public int UpdateCount;
        public int ExitCount;

        public UniTask EnterAsync() { EnterCount++; return UniTask.CompletedTask; }
        public void Update() { UpdateCount++; }
        public UniTask ExitAsync() { ExitCount++; return UniTask.CompletedTask; }
    }

    public class FakeAppStateB : IAppState
    {
        public int EnterCount;
        public int UpdateCount;
        public int ExitCount;
        public UniTaskCompletionSource Entered { get; } = new();

        public UniTask EnterAsync() { EnterCount++; Entered.TrySetResult(); return UniTask.CompletedTask; }
        public void Update() { UpdateCount++; }
        public UniTask ExitAsync() { ExitCount++; return UniTask.CompletedTask; }
    }

    public class PendingFakeAppState : IAppState
    {
        private readonly UniTaskCompletionSource _enterSource = new();
        private readonly UniTaskCompletionSource _exitSource = new();
        public int EnterCount;
        public int ExitCount;

        public UniTask EnterAsync() { EnterCount++; return _enterSource.Task; }
        public void Update() { }
        public UniTask ExitAsync() { ExitCount++; return _exitSource.Task; }

        public void CompleteEnter() => _enterSource.TrySetResult();
        public void CompleteExit() => _exitSource.TrySetResult();
    }

    public class FakeGameStateA : IGameState
    {
        public int EnterCount;
        public UniTask EnterAsync() { EnterCount++; return UniTask.CompletedTask; }
        public void Update() { }
        public UniTask ExitAsync() => UniTask.CompletedTask;
    }

    public class FakeStateResolver<TState> : IStateResolver<TState> where TState : IState
    {
        private readonly Dictionary<Type, TState> _states = new();

        public void AddState<TConcrete>(TConcrete state) where TConcrete : class, TState
        {
            _states[typeof(TConcrete)] = state;
        }

        public TConcrete GetState<TConcrete>() where TConcrete : class, TState
        {
            return _states.TryGetValue(typeof(TConcrete), out var state)
                ? (TConcrete)state
                : throw new InvalidOperationException($"State {typeof(TConcrete)} not registered");
        }
    }

    public class FakeValidator<TState> : IStateTransitionValidator<TState> where TState : IState
    {
        private readonly List<Type> _nextBlockedTypes = new();

        public void BlockTransitionTo<TConcrete>() where TConcrete : TState
        {
            _nextBlockedTypes.Add(typeof(TConcrete));
        }

        public bool IsValid<TNext>(TState currentState) where TNext : TState => !_nextBlockedTypes.Contains(typeof(TNext));
    }

    public class StateMachineEnterTests
    {
        [Test]
        public void EnterAsync_CallsExitOnCurrentStateBeforeEnteringNewOne()
        {
            var stateA = new FakeAppStateA();
            var stateB = new FakeAppStateB();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<FakeAppStateA>(stateA);
            resolver.AddState<FakeAppStateB>(stateB);
            var validator = new FakeValidator<IAppState>();
            var sm = new StateMachine<IAppState>(resolver, validator);

            sm.EnterAsync<FakeAppStateA>().GetAwaiter().GetResult();
            sm.EnterAsync<FakeAppStateB>().GetAwaiter().GetResult();

            Assert.That(stateA.ExitCount, Is.EqualTo(1));
            Assert.That(stateB.EnterCount, Is.EqualTo(1));
        }

    }

    public class StateMachineUpdateTests
    {
        [Test]
        public void Update_DelegatesToCurrentState()
        {
            var state = new FakeAppStateA();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<FakeAppStateA>(state);
            var validator = new FakeValidator<IAppState>();
            var sm = new StateMachine<IAppState>(resolver, validator);
            sm.EnterAsync<FakeAppStateA>().GetAwaiter().GetResult();

            sm.Update();
            sm.Update();
            sm.Update();

            Assert.That(state.UpdateCount, Is.EqualTo(3));
        }

    }

    public class StateMachineExitTests
    {
        [Test]
        public void ExitAsync_AfterExit_UpdateDoesNotCallPreviousState()
        {
            var stateA = new FakeAppStateA();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<FakeAppStateA>(stateA);
            var validator = new FakeValidator<IAppState>();
            var sm = new StateMachine<IAppState>(resolver, validator);
            sm.EnterAsync<FakeAppStateA>().GetAwaiter().GetResult();
            sm.ExitAsync().GetAwaiter().GetResult();

            sm.Update();

            Assert.That(stateA.ExitCount, Is.EqualTo(1));
            Assert.That(stateA.UpdateCount, Is.EqualTo(0));
        }
    }

    public class StateMachineValidatorTests
    {
        [Test]
        public void EnterAsync_BlockedByValidator_DoesNotTransition()
        {
            var stateA = new FakeAppStateA();
            var stateB = new FakeAppStateB();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<FakeAppStateA>(stateA);
            resolver.AddState<FakeAppStateB>(stateB);
            var validator = new FakeValidator<IAppState>();
            validator.BlockTransitionTo<FakeAppStateB>();
            var sm = new StateMachine<IAppState>(resolver, validator);

            sm.EnterAsync<FakeAppStateB>().GetAwaiter().GetResult();

            Assert.That(stateB.EnterCount, Is.EqualTo(0));
        }

    }

    public class StateMachineContextTests
    {
        [UnityTest]
        public IEnumerator EnterAndExitCalls_AreQueuedUntilCurrentTransitionCompletes() => UniTask.ToCoroutine(async () =>
        {
            var stateA = new PendingFakeAppState();
            var stateB = new FakeAppStateB();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<PendingFakeAppState>(stateA);
            resolver.AddState<FakeAppStateB>(stateB);
            var validator = new FakeValidator<IAppState>();
            var sm = new StateMachine<IAppState>(resolver, validator);
            var enterA = sm.EnterAsync<PendingFakeAppState>();
            var exitA = sm.ExitAsync();
            var enterB = sm.EnterAsync<FakeAppStateB>();

            await UniTask.Yield();
            Assert.That(stateA.EnterCount, Is.EqualTo(1));
            Assert.That(stateA.ExitCount, Is.EqualTo(0));
            Assert.That(stateB.EnterCount, Is.EqualTo(0));

            stateA.CompleteEnter();
            await enterA;
            await UniTask.Yield();
            Assert.That(stateA.ExitCount, Is.EqualTo(1));
            Assert.That(stateB.EnterCount, Is.EqualTo(0));

            stateA.CompleteExit();
            await exitA;
            await stateB.Entered.Task;
            await enterB;
            Assert.That(stateB.EnterCount, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator RequestTransition_FromStateEntry_QueuesTheWholeChain() => UniTask.ToCoroutine(async () =>
        {
            var trace = new List<string>();
            var load = new QueuedLoadState(trace);
            var menu = new QueuedMenuState(trace);
            var gameplay = new QueuedGameplayState(trace);
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<QueuedLoadState>(load);
            resolver.AddState<QueuedMenuState>(menu);
            resolver.AddState<QueuedGameplayState>(gameplay);
            var sm = new StateMachine<IAppState>(resolver, new FakeValidator<IAppState>());
            var context = new StateMachineContext<IAppState>(sm);
            load.RequestNext = () => context.RequestTransition<QueuedMenuState>();
            menu.RequestNext = () => context.RequestTransition<QueuedGameplayState>();

            var initialTransition = sm.EnterAsync<QueuedLoadState>();
            Assert.That(trace, Is.EqualTo(new[] { "load:enter:start" }));

            load.CompleteEnter();
            await gameplay.Entered.Task;
            await initialTransition;

            Assert.That(trace, Is.EqualTo(new[]
            {
                "load:enter:start",
                "load:enter:end",
                "load:exit",
                "menu:enter:start",
                "menu:enter:end",
                "menu:exit",
                "gameplay:enter:start",
                "gameplay:enter:end",
            }));
        });

        [UnityTest]
        public IEnumerator RequestTransition_ContinuesQueueAfterStateEntryThrows() => UniTask.ToCoroutine(async () =>
        {
            var failing = new ThrowingEnterAppState();
            var stateB = new FakeAppStateB();
            var resolver = new FakeStateResolver<IAppState>();
            resolver.AddState<ThrowingEnterAppState>(failing);
            resolver.AddState<FakeAppStateB>(stateB);
            var sm = new StateMachine<IAppState>(resolver, new FakeValidator<IAppState>());
            var context = new StateMachineContext<IAppState>(sm);
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);
            failing.RequestNext = () => context.RequestTransition<FakeAppStateB>();

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                context.RequestTransition<ThrowingEnterAppState>();
                await stateB.Entered.Task;
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1));
            Assert.That(stateB.EnterCount, Is.EqualTo(1));
            Assert.That(failing.ExitCount, Is.EqualTo(1), "failed entry gets a best-effort exit");
        });
    }

    public class ThrowingEnterAppState : IAppState
    {
        public int ExitCount;
        public Action RequestNext { get; set; }

        public UniTask EnterAsync()
        {
            RequestNext?.Invoke();
            throw new InvalidOperationException("enter failed");
        }

        public void Update() { }
        public UniTask ExitAsync() { ExitCount++; return UniTask.CompletedTask; }
    }

    public abstract class QueuedAppState : IAppState
    {
        private readonly string _name;
        private readonly List<string> _trace;
        private readonly UniTaskCompletionSource _enterSource = new();
        private readonly bool _waitForEnter;

        public Action RequestNext { get; set; }

        public UniTaskCompletionSource Entered { get; } = new();

        protected QueuedAppState(string name, List<string> trace, bool waitForEnter = false)
        {
            _name = name;
            _trace = trace;
            _waitForEnter = waitForEnter;
        }

        public async UniTask EnterAsync()
        {
            _trace.Add($"{_name}:enter:start");
            RequestNext?.Invoke();
            if (_waitForEnter)
                await _enterSource.Task;
            _trace.Add($"{_name}:enter:end");
            Entered.TrySetResult();
        }

        public void Update() { }

        public UniTask ExitAsync()
        {
            _trace.Add($"{_name}:exit");
            return UniTask.CompletedTask;
        }

        public void CompleteEnter() => _enterSource.TrySetResult();
    }

    public sealed class QueuedLoadState : QueuedAppState
    {
        public QueuedLoadState(List<string> trace) : base("load", trace, waitForEnter: true) { }
    }

    public sealed class QueuedMenuState : QueuedAppState
    {
        public QueuedMenuState(List<string> trace) : base("menu", trace) { }
    }

    public sealed class QueuedGameplayState : QueuedAppState
    {
        public QueuedGameplayState(List<string> trace) : base("gameplay", trace) { }
    }

    public class StateMachineIsolationTests
    {
        [Test]
        public void SeparateStateMachines_DoNotInterfereWithEachOther()
        {
            var appStateA = new FakeAppStateA();
            var appResolver = new FakeStateResolver<IAppState>();
            appResolver.AddState<FakeAppStateA>(appStateA);
            var appValidator = new FakeValidator<IAppState>();
            var appSm = new StateMachine<IAppState>(appResolver, appValidator);

            var gameStateA = new FakeGameStateA();
            var gameResolver = new FakeStateResolver<IGameState>();
            gameResolver.AddState<FakeGameStateA>(gameStateA);
            var gameValidator = new FakeValidator<IGameState>();
            var gameSm = new StateMachine<IGameState>(gameResolver, gameValidator);

            appSm.EnterAsync<FakeAppStateA>().GetAwaiter().GetResult();
            gameSm.EnterAsync<FakeGameStateA>().GetAwaiter().GetResult();

            Assert.That(appStateA.EnterCount, Is.EqualTo(1));
            Assert.That(gameStateA.EnterCount, Is.EqualTo(1));
        }
    }
}
