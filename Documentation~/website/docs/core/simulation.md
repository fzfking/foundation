---
title: Simulation
sidebar_position: 4
---

# Simulation

A simulation loop with a variable or fixed step, independent of Unity's `FixedUpdate`, so gameplay can be paused,
resumed and rate-controlled through the time contracts. The UI observes simulation state but never drives it.

The runner advances gameplay from frame time or from explicit steps. It is not a deterministic simulation, lockstep
or network synchronization layer; see [Limits](#limits).

| Type | Role |
| --- | --- |
| `ISimulationSystem` | One slice of simulation logic. `Order` sorts systems; `Tick(in SimulationTick)` advances it. |
| `SimulationRunner` | Ticks every system in order. `Start`, `Pause`, `Resume`, `Stop`, `Tick`, `Step`. |
| `SimulationTickMode` | `Automatic` by default. `Manual` advances only through `Step`. |
| `SimulationBacklogPolicy` | `Bounded` by default: carry a limited catch-up and report the rest as dropped. `Preserve` keeps every unprocessed step for later frames. |
| `SimulationTimers` | Tick-based timers, cleared by `Start` and `Stop`; due timers run after the systems of the step. |
| `ISimulationTimerHandle` | Cancels one timer with `Dispose`. |
| `ISimulationClock` | How much time passed since the last `Tick`. `TimeProviderSimulationClock` uses unscaled frame time times `ITimeController.SimulationScale`. |
| `SimulationTick` | What a system receives: `Index`, `DeltaTime`, `ElapsedTime`. |
| `SimulationContext` | The runner's state: `State`, `TickIndex`, `ElapsedTime`, `DroppedTime`. |

Systems run by `Order` ascending. Ties are sorted by full type name, and instances of the same type keep their
registration order. Use one system per concern, not one per entity: do not give every production row or enemy its
own `Update`.

## Example

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.Simulation;
using Foundation.Timing;

public sealed class ProductionState
{
    public double Progress { get; set; }
}

public sealed class ProductionSystem : ISimulationSystem
{
    private readonly ProductionState _state;

    public ProductionSystem(ProductionState state) => _state = state;

    public int Order => 0;

    public void Tick(in SimulationTick tick) => _state.Progress += tick.DeltaTime;
}

public sealed class SimulationDriver : ITickableEntryPoint
{
    private readonly SimulationRunner _runner;

    public SimulationDriver(ProductionSystem production, ITimeProvider time, ITimeController timeController)
    {
        _runner = new SimulationRunner(
            new ISimulationSystem[] { production },
            new TimeProviderSimulationClock(time, timeController),
            fixedDeltaTime: 0.1f);
        _runner.Start();
    }

    public void Tick() => _runner.Tick();
}

public sealed class SimulationInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<ProductionState>(ServiceLifetime.Singleton);
        registry.Register<ProductionSystem>(ServiceLifetime.Singleton);
        registry.RegisterEntryPoint<SimulationDriver>();
    }
}
```

`ITimeProvider` and `ITimeController` must be registered in an outer scope (see [Time, scheduling and randomness](time-scheduling-random)).

## Automatic and manual ticking

A new runner is stopped. `Start()` resets the context and timers, then sets the state to `Running`. `Tick()` does
nothing unless the runner is running in automatic mode.

In `Automatic` mode each `Tick()` advances by `ISimulationClock.GetDeltaTime()`, in whole fixed steps when
`fixedDeltaTime` is set. `SimulationTicker`, registered by `UseSimulation`, calls `Start()` and then `Tick()` every
frame.

For manual control, pass `tickMode: SimulationTickMode.Manual` and a positive `fixedDeltaTime`. `Tick()` does
nothing in this mode; `Step(steps)` runs exactly that many whole fixed steps (one by default). Use manual mode when a
lockstep controller, replay tool or test decides when each step happens. `Step()` throws in automatic mode, does
nothing while the runner is stopped or paused, and rejects a negative count. The backlog policy does not apply to
manual steps.

## Fixed steps, budget and catch-up

Without `fixedDeltaTime`, every automatic `Tick` is one step of the clock's delta. With a `fixedDeltaTime`, a frame
runs whole steps, at most `MaxStepsPerTick` of them (8 by default).

Time left after that budget follows `BacklogPolicy`:

- `Bounded` (default): at most `MaxCatchUpTime` of simulated time is carried into later frames. The rest is skipped
  and added to `SimulationContext.DroppedTime`, so a simulation that runs slower than its clock is visible. With the
  default `MaxCatchUpTime` of zero, time left after the step budget is skipped instead of replayed.
- `Preserve`: every unprocessed whole step stays in the accumulator and runs on later frames, still at most
  `MaxStepsPerTick` per frame. Nothing is added to `DroppedTime`. A simulation that cannot keep up builds an
  unbounded backlog.

| Game | Setting |
| --- | --- |
| An action game | Keep the defaults: time beyond the frame's step budget is skipped instead of carried forward. |
| A game with fast-forward | Raise `maxStepsPerTick` to the steps the fastest speed needs per frame (x5 at 20 fps with 1/60 s steps needs 15). |
| A simulation that must not lose time (a colony sim) | Use `SimulationBacklogPolicy.Preserve`. With `Bounded`, time beyond `maxCatchUpTime` is dropped. |

## Simulation timers

Timers are available through `runner.Timers`, or as a `SimulationTimers` service from a scope that called
`UseSimulation`. A delay counts whole simulation ticks, not seconds: with a fixed step each step is a tick; without
one, each `Tick()` is a tick.

`Schedule(delayTicks, callback)` with zero delay runs on the next tick. A timer scheduled during a tick never runs in
that same tick. Timers due on the same tick run in scheduling order, and all due timers run after the systems of the
step. A due tick that would overflow the tick counter is rejected.

Disposing the handle cancels that timer. `Clear()` cancels every timer, including ones already due in the current
step. `Start()` and `Stop()` clear timers the same way, so no timer outlives a run. A timer scheduled before `Start()`
is discarded by that start; schedule after the start or from a system's first tick. Pause keeps timers, and resume
continues their tick count.

Timers are not serialized. The runner neither saves nor restores callbacks. A game that saves events must schedule
them again after every start.

## Order, pause and stop

One step runs the systems in the order above, then the timers due on that tick. Both kinds of callback may call
`Pause`, `Resume`, `Stop` or `Start`.

`Pause` from a callback lets the current step finish, commits its `TickIndex`, and stops the rest of the batch. A
`Resume` later in that same step changes the state back to `Running` but does not restart the batch. Time spent
paused is not replayed after `Resume`.

`Stop` resets the context to `TickIndex` zero and clears timers. From a callback it aborts the step immediately: no
later system or timer of that step runs, and no `TickIndex` increment follows. `Start` also resets the context and
clears timers before running.

Calling `Tick` or `Step` from a system or timer callback throws `InvalidOperationException`, because the runner
cannot advance from inside its own step. `Pause`, `Resume`, `Start` and `Stop` are allowed there.

Only an exception from a system or timer stops the run. Its failure propagates to the caller, and the runner does
not roll back game state, so the game must recover any partially changed data before starting again. An exception
from the clock, or a non-finite clock delta, propagates without changing the runner's state.

## Pausing and speed

`SimulationRunner.Pause()` and `Resume()` stop and restart ticking without resetting state; `Stop()` resets the
context and timers. `ITimeController.SetSimulationScale(2f)` doubles the speed the clock reports; `0` freezes it
without touching animation time (`SetAnimationScale` is what changes Unity's `timeScale`).

## Limits

`TimeProviderSimulationClock` reports unscaled frame delta times `ITimeController.SimulationScale`, so the
simulation follows frame time. That does not make the game deterministic. The runner has no input capture, rollback,
state hashing, lockstep or network synchronization, and it makes no promise that physics stays stable across frame
rates or devices. `DeterministicRandomService` makes the random sequence reproducible, which is one input to a
deterministic game, not the whole system.

For a replay or lockstep game, use manual mode, fixed steps and your own input and state capture. The runner supplies
ordered steps and tick-based timers; the game owns the rest.
