---
title: State machine
sidebar_position: 3
---

# State machine

A small generic state machine for startup sequences, screen flow or any explicit finite-state flow. States are resolved
from the DI scope, so they take constructor dependencies like any other service.

| Type | Role |
| --- | --- |
| `IState` | One state: `EnterAsync()`, `Update()`, `ExitAsync()`. |
| `IStateMachine<TState>` / `StateMachine<TState>` | Owns the current state. `EnterAsync<TConcrete>()` exits the current state, then enters the next. |
| `IStateResolver<TState>` / `DiStateResolver<TState>` | Creates the next state instance. `DiStateResolver` resolves it from the scope. |
| `IStateTransitionValidator<TState>` | Optional guard. `AlwaysValidStateTransitionValidator<TState>` allows everything. |
| `IStateMachineContext<TState>` | What a state uses to ask for the next transition: `RequestTransition<TConcrete>()`. |

If the validator rejects a transition, `EnterAsync` returns without changing anything.

## A startup flow

Group the states of one machine under a marker interface, so the machine only accepts its own states:

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.StateMachine;

public interface IAppState : IState
{
}

public sealed class LoadContentState : IAppState
{
    private readonly IStateMachineContext<IAppState> _machine;

    public LoadContentState(IStateMachineContext<IAppState> machine) => _machine = machine;

    public async UniTask EnterAsync()
    {
        await UniTask.Delay(100);             // load configs, warm services...
        _machine.RequestTransition<MenuState>();
    }

    public void Update()
    {
    }

    public UniTask ExitAsync() => UniTask.CompletedTask;
}

public sealed class MenuState : IAppState
{
    public UniTask EnterAsync() => UniTask.CompletedTask;

    public void Update()
    {
    }

    public UniTask ExitAsync() => UniTask.CompletedTask;
}
```

Calls to `EnterAsync`, `ExitAsync` and `RequestTransition` pass through one FIFO queue owned by the state machine, so
transitions never overlap. A request made inside a state's `EnterAsync` waits until the current transition completes,
including the previous state's exit. This also applies to the initial state when it is entered directly through
`IStateMachine.EnterAsync`. The validator checks each transition when the queue processes it, using the then-current
state. Direct calls to `IStateMachine.EnterAsync` and `ExitAsync` return a faulted `UniTask` if the operation fails.
`RequestTransition` does not return a task; failures from those fire-and-forget requests are reported through UniTask's
unobserved task exception handler, and the queue continues with the next request.

Use `IStateMachineContext.RequestTransition` from a state lifecycle method to request a follow-up transition. Do not
await `IStateMachine.EnterAsync` or `ExitAsync` on the same machine from inside its current state's `EnterAsync` or
`ExitAsync`: those calls wait in the queue behind the operation that is awaiting them.

## Wiring it

Use `registry.UseStateMachine<TState>()` for the default registration, or `registry.UseStateMachine<TState,
TValidator>()` for a custom transition validator. The example below shows the equivalent manual registration; register
your concrete states separately in either case.

```csharp compile
public sealed class AppStatesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<DiStateResolver<IAppState>>(ServiceLifetime.Singleton).As<IStateResolver<IAppState>>();
        registry.Register<AlwaysValidStateTransitionValidator<IAppState>>(ServiceLifetime.Singleton)
            .As<IStateTransitionValidator<IAppState>>();
        registry.Register<StateMachine<IAppState>>(ServiceLifetime.Singleton).As<IStateMachine<IAppState>>();
        registry.Register<StateMachineContext<IAppState>>(ServiceLifetime.Singleton).As<IStateMachineContext<IAppState>>();

        registry.Register<LoadContentState>(ServiceLifetime.Singleton);
        registry.Register<MenuState>(ServiceLifetime.Singleton);

        registry.RegisterEntryPoint<AppStateDriver>();
    }
}

public sealed class AppStateDriver : IAsyncStartableEntryPoint, ITickableEntryPoint
{
    private readonly IStateMachine<IAppState> _machine;

    public AppStateDriver(IStateMachine<IAppState> machine) => _machine = machine;

    public UniTask StartAsync(CancellationToken cancellationToken) => _machine.EnterAsync<LoadContentState>();

    public void Tick() => _machine.Update();
}
```

## Transition rules

Implement `IStateTransitionValidator<TState>` to restrict the graph. `IsValid<TNext>(currentState)` receives the current
state (`null` before the first transition):

```csharp compile
public sealed class AppTransitionValidator : IStateTransitionValidator<IAppState>
{
    public bool IsValid<TNext>(IAppState currentState) where TNext : IAppState
    {
        // The menu can only be entered after content has been loaded.
        return typeof(TNext) != typeof(MenuState) || currentState is LoadContentState;
    }
}
```

Register it in place of `AlwaysValidStateTransitionValidator<IAppState>`.
