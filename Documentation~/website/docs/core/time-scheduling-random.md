---
title: Time, scheduling and randomness
sidebar_position: 5
---

# Time, scheduling and randomness

Three small contracts that keep gameplay code independent of `UnityEngine.Time`, coroutines and `System.Random`, and
therefore testable.

## Time

| Contract | Default | Notes |
| --- | --- | --- |
| `ITimeProvider` | `SystemTimeProvider` | `UtcNow`, `Time`, `DeltaTime`, `UnscaledDeltaTime`, `ElapsedSinceBoot` (the monotonic device clock in an Android player; process-relative elsewhere, including the Editor). |
| `ITimeController` | `UnityTimeController` | `SimulationScale` for the simulation clock, `AnimationScale` which sets Unity's `Time.timeScale`. |

Register them in the application scope:

```csharp compile
using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.Randomization;
using Foundation.Scheduling;
using Foundation.Timing;

public sealed class TimeInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<SystemTimeProvider>(ServiceLifetime.Singleton).As<ITimeProvider>();
        registry.Register<UnityTimeController>(ServiceLifetime.Singleton).As<ITimeController>();
        registry.RegisterScheduler();
    }
}
```

`ITimeProvider.UtcNow` is the wall clock. A game that grants offline income should treat it as untrusted input.

## Scheduler

`IScheduler` runs one-shot and repeating callbacks from one shared timer, instead of each system writing its own.

```csharp compile
public sealed class AutoSaveTimer : IDisposable
{
    private readonly IScheduledHandle _handle;

    public AutoSaveTimer(IScheduler scheduler)
    {
        _handle = scheduler.ScheduleRepeating(intervalSeconds: 30f, Save);
        scheduler.ScheduleOnce(delaySeconds: 5f, () => UnityEngine.Debug.Log("five seconds in"));
    }

    public void Dispose() => _handle.Dispose();

    private void Save()
    {
    }
}
```

`ScheduleRepeating` uses fixed delay by default. A full interval starts at the tick that invokes the callback, so a hitch fires once
instead of replaying the missed calls in a burst. The scheduler counts unscaled frame time, so timers keep running
when the simulation is slowed or paused.

- A callback that throws does not stop the other callbacks of that tick. Its failure is rethrown after they all ran:
  one exception as itself, several as an `AggregateException`.
- Disposing the returned handle cancels that callback. Disposing the scheduler cancels all of them.
- A callback scheduled during a tick starts on the next tick.
- Calling `Scheduler.Tick()` from a callback is ignored. The frame's time is already advanced, and a nested call
  would fire the same timers twice.
- `RegisterScheduler()` registers the one ticking `Scheduler` in the application scope. A [game context](../app/game-contexts)
  registers `RegisterScopedScheduler()` in its own scope: that `IScheduler` schedules on the application's scheduler and
  cancels everything it scheduled when the context's scope is disposed, so no timer calls into a context that is gone.
- An optional `ITimeIntegrityMonitor` receives the scheduler's unscaled frame delta and elapsed UTC time since the
  previous tick; the package provides no implementation.

### Fixed-rate callbacks

Fixed delay and fixed rate are different. A timer that must keep its rate uses the optional `IFixedRateScheduler`
capability through `ScheduleFixedRateRepeating`, available as an extension on `IScheduler`:

```csharp compile
public sealed class TickPulse : IDisposable
{
    private readonly IScheduledHandle _handle;

    public TickPulse(IScheduler scheduler)
    {
        _handle = scheduler.ScheduleFixedRateRepeating(
            intervalSeconds: 0.25f,
            callback: Pulse,
            maxCallbacksPerTick: 2,
            backlogPolicy: FixedRateBacklogPolicy.Bounded);
    }

    public void Dispose() => _handle.Dispose();

    private void Pulse()
    {
    }
}
```

The interval is measured between due times, not from the end of a callback. One scheduler tick runs at most
`maxCallbacksPerTick` callbacks for that timer; the rest follow the backlog policy.

| Policy | What happens to callbacks a tick cannot run |
| --- | --- |
| `Bounded` (default) | Drops them and resumes at the next interval boundary, keeping the timer's phase. |
| `Preserve` | Keeps them as debt and runs them on later ticks, still at most the cap per tick. |

`Preserve` can accumulate unlimited debt when the callback cannot keep up. If that debt becomes non-finite, or its
size is so large that the interval is below the precision needed to reduce it, the scheduler deactivates that timer
instead of dropping the debt silently or throwing every frame. The tick then reports an `InvalidOperationException`
with the other callback failures, and the remaining timers still run. Use `Bounded` for a timer that can fall behind.

The extension defaults to `maxCallbacksPerTick: 1` and `FixedRateBacklogPolicy.Bounded`. If an `IScheduler`
implementation does not also implement `IFixedRateScheduler`, the extension throws `NotSupportedException` instead of
silently falling back to the slower fixed delay. `ScopedScheduler` supports both.

## Randomness

`IRandomService` is the one randomness contract gameplay depends on, so tests can substitute a seeded generator.

| Implementation | Use |
| --- | --- |
| `SystemRandomService` | Default, `System.Random`-backed. |
| `DeterministicRandomService` | xoshiro256** seeded through SplitMix64. The sequence of a seed is the same on every device. For simulation and anything a save or a replay must reproduce. |

```csharp compile
public sealed class LootRoller
{
    private readonly IRandomService _random;

    public LootRoller(IRandomService random) => _random = random;

    public string RollDrop()
    {
        return _random.NextWeighted(new[]
        {
            new WeightedItem<string>("common", 70),
            new WeightedItem<string>("rare", 25),
            new WeightedItem<string>("legendary", 5),
        });
    }

    public bool RollCrit(int critChancePercent) => _random.RollDice(100).IsAtMost(critChancePercent);

    public void Shuffle(IList<int> deck) => _random.Shuffle(deck);
}
```

`RollDice(sides)` returns a `DiceRoll` with `Is`, `IsAbove`, `IsAtLeast`, `IsBelow`, `IsAtMost`.

A deterministic random stream is one part of a reproducible simulation. It does not make frame timing, input or
physics deterministic by itself; see [Simulation](simulation#limits).

### Reproducible streams

`DeterministicRandomService` can save and restore its exact position and fork independent streams:

```csharp compile
public static class DeterministicRandomExample
{
    public static void Run()
    {
        IDeterministicRandom random = new DeterministicRandomService(seed: 42);

        var checkpoint = random.CaptureState();
        var first = random.NextInt(0, 1000);
        random.RestoreState(checkpoint);
        var again = random.NextInt(0, 1000);       // equals first

        // An independent stream that depends only on the seed and the name: adding draws to the loot stream
        // never shifts the sequence of another system.
        var loot = random.Fork("loot");
        UnityEngine.Debug.Log($"{first} {again} {loot.NextInt(0, 10)}");
    }
}
```
