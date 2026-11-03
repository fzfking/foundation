---
title: Dependency injection
sidebar_position: 1
---

# Dependency injection

Core services do not depend on a specific DI library. Features register through `IServiceRegistry` and resolve through
`IServiceResolver`, and a DI **adapter** maps that onto the container the project uses (the package ships
[VContainer](../adapters/vcontainer)).

| Type | Role |
| --- | --- |
| `IServiceRegistry` | What an installer registers into: types, factories, instances, entry points, build and dispose callbacks. |
| `IServiceBinding` | Returned by a registration. `.As<T>()` and `.AsSelf()` choose what the registration is exposed as. |
| `IServiceResolver` | `Resolve<T>()` and `TryResolve<T>(out T)`. |
| `IServiceScope` | A resolver with an explicit lifetime: `CreateScope(configure)` makes a child that sees its parent and shadows it. |
| `IFeatureInstaller` | A feature's registrations, so a composition root applies a list instead of one monolith. |
| `IGameObjectInjector` | Gives a freshly created `GameObject` the services of its scope. |

## Lifetimes

`ServiceLifetime` is `Transient` (a new instance per resolve), `Scoped` (one per scope) or `Singleton` (one instance per
registering scope, shared with its child scopes).

A registration is exposed as its own type until `As` says otherwise. Once you call `As<T>()`, the registration is
exposed as `T` only; add `.AsSelf()` to keep the concrete type resolvable too.
Entry-point registrations are an exception: `RegisterEntryPoint<T>()` exposes the type and all its implemented interfaces.

## Feature installers

An installer is the unit of extension. It receives an `IServiceRegistry` and never mentions the container.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using UnityEngine;

public interface IDamageCalculator
{
    int Calculate(int attack, int defence);
}

public sealed class LinearDamageCalculator : IDamageCalculator
{
    public int Calculate(int attack, int defence) => Mathf.Max(0, attack - defence);
}

public sealed class CombatLog
{
    public List<string> Lines { get; } = new();
}

public sealed class CombatInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<LinearDamageCalculator>(ServiceLifetime.Singleton).As<IDamageCalculator>();
        registry.Register<CombatLog>(ServiceLifetime.Singleton);
        registry.RegisterInstance(new CombatSettings(maxRounds: 10));
    }
}

public sealed class CombatSettings
{
    public CombatSettings(int maxRounds) => MaxRounds = maxRounds;

    public int MaxRounds { get; }
}
```

## Wiring helpers: `UseXxx`

The package's systems have a standard registration, so a composition root does not repeat it. Every `UseXxx` extension
on `IServiceRegistry` registers one system with its usual implementation and returns the registry, so the calls chain.
They are registration helpers, not a separate layer: they can register services, instances and build callbacks.
You can skip a helper and register the pieces yourself when you need a different implementation. The older `RegisterXxx` methods stay for the pieces that have no
chainable form.

```csharp compile
using Foundation.DI;
using Foundation.ErrorHandling;
using Foundation.FeatureFlags;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Rewards;
using Foundation.Scheduling;
using Foundation.Timing;

public sealed class CoreServicesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry
            .UseTime()
            .UseLogging()
            .UseMessaging()
            .UseErrorHandling()
            .UseScheduler()
            .UseRewards()
            .UseFeatureFlags(new StaticFlags());
    }
}

public sealed class StaticFlags : IFeatureFlagSource
{
    public Cysharp.Threading.Tasks.UniTask<bool?> TryGetAsync(
        string featureId,
        System.Threading.CancellationToken cancellationToken = default)
        => Cysharp.Threading.Tasks.UniTask.FromResult<bool?>(null);
}
```

| Helper | Registers | Needs |
| --- | --- | --- |
| `UseTime()` | `ITimeProvider`, `ITimeController` | |
| `UseLogging(outputs)` | `IAppLogger` writing to the outputs, or to the Unity console | |
| `UseMessaging(options)` | event, command and query buses; `MessagingOptions` sets their [ordering](messaging#ordering) | |
| `UseErrorHandling()` | `IErrorHandler` | logger, event bus |
| `UseScheduler()`, `UseScopedScheduler()` | the application scheduler, or one that cancels its callbacks with the scope | `ITimeProvider` |
| `UseStateMachine<TState>()`, `UseStateMachine<TState, TValidator>()` | a state machine whose states are resolved from the scope | the states, registered by you |
| `UseSimulation(...)`, `UseSimulationSystem<T>()` | the simulation runner, ticked each frame, over every registered system | `UseTime()`, a container that resolves collections |
| `UseRewards()`, `UseRewardHandler<TReward, THandler>()` | the reward service; a handler connected when the scope is built | |
| `UseFeatureFlags<TSource>()`, `UseFeatureFlags(source)` | `IFeatureFlagService` over a source | |
| `UseLocalization<TTableSource, TFormatter>(settings)`, `UseLocalization<TTableSource>(settings)` | the localization service and its source; the second leaves the formatter to you | |
| `UseConfigs()`, `UseConfigGroups(installer)` | the config runtime; the groups declared once the scope is built | serializer, asset provider |
| `UseSaveStorage(defaultFileName)`, `UseSaveOrchestrator(...)`, `UseSaveModule<T>()` | save plumbing (serializer, local file, composite repository); the orchestrator over every module; one module | serializer, a container that resolves collections |
| `UseUiRouter(definitions, root)` | the uGUI router | asset provider |
| `UseMemoryPack()`, `UseMessagePack(resolvers)` | the `ISerializer` on that library | the library's adapter assembly |
| `UseSmartFormat(configure)` | the `ILocalizationFormatter` on SmartFormat | `FOUNDATION_SMARTFORMAT` |
| `UseAddressables()` | the Addressables `IAssetProvider` behind a queue, and the object spawner | logger |

`UseSaveOrchestrator()` uses the registered `GameContextSaveProfile` when present: its slot, version and migrations
override the helper defaults. Without a profile it uses the default slot and the supplied version/migrations.

A helper that needs something registered elsewhere says so in the table and in its XML summary; the container reports a
missing prerequisite when it first resolves the service.

## Optional dependencies

Some package constructors have optional parameters. When a dependency may be absent, a factory that calls `TryResolve`
makes the fallback explicit and works with containers that do not honor optional constructor parameters:

```csharp compile
public sealed class Announcer
{
    private readonly CombatLog _log;

    public Announcer(CombatLog log, IDamageCalculator calculator)
    {
        _log = log;
        Calculator = calculator;
    }

    public IDamageCalculator Calculator { get; }

    public void Announce(string line) => _log.Lines.Add(line);
}

public sealed class AnnouncerInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register(
            resolver => new Announcer(
                resolver.Resolve<CombatLog>(),
                resolver.TryResolve<IDamageCalculator>(out var calculator) ? calculator : new LinearDamageCalculator()),
            ServiceLifetime.Singleton);
    }
}
```

## Entry points

An entry point is a service whose lifecycle is managed by the DI adapter. `RegisterEntryPoint<T>()` registers it, and
the adapter calls its implemented `Start`, `StartAsync` or `Tick` methods as the scope is created and runs. Disposing the
scope cancels async startup, stops further ticks and disposes owned `IDisposable` services; there is no shared `Stop` method.

| Interface | Called |
| --- | --- |
| `IStartableEntryPoint.Start()` | once, when the scope is built |
| `IAsyncStartableEntryPoint.StartAsync(ct)` | once, when the scope is built; canceled when the scope is disposed |
| `ITickableEntryPoint.Tick()` | every frame |

```csharp compile
public sealed class CombatTicker : IStartableEntryPoint, ITickableEntryPoint
{
    private readonly CombatLog _log;
    private float _elapsed;

    public CombatTicker(CombatLog log) => _log = log;

    public void Start() => _log.Lines.Add("combat started");

    public void Tick() => _elapsed += Time.deltaTime;
}

public sealed class CombatTickerInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry) => registry.RegisterEntryPoint<CombatTicker>();
}
```

Use `RegisterBuildCallback` and `RegisterDisposeCallback` for one-off wiring that must happen after the container is
built or before it is torn down (the message-handler registration is built on them).

## Child scopes

`IServiceScope.CreateScope` makes a child that sees its parent and can shadow its registrations. Disposing the child
disposes what it owns. [Game contexts](../app/game-contexts) are child scopes.

```csharp compile
public sealed class Match
{
}

public static class ScopeExample
{
    public static Match PlayOneMatch(IServiceScope applicationScope)
    {
        using var matchScope = applicationScope.CreateScope(registry => registry.Register<Match>(ServiceLifetime.Scoped));
        return matchScope.Resolve<Match>();
    }
}
```

## Factories

`IFactory<T>` and `IFactory<TParam, T>` are factory contracts. `ResolvedFactory<T>` is a base that holds the scope's
resolver, for a factory that creates objects at runtime:

```csharp compile
public sealed class Enemy
{
    public Enemy(IDamageCalculator calculator) => Calculator = calculator;

    public IDamageCalculator Calculator { get; }
}

public sealed class EnemyFactory : ResolvedFactory<Enemy>
{
    public EnemyFactory(IServiceResolver resolver) : base(resolver)
    {
    }

    public override Enemy Create() => new Enemy(Resolver.Resolve<IDamageCalculator>());
}

public sealed class EnemyFactoryInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
        => registry.Register<EnemyFactory>(ServiceLifetime.Singleton).As<IFactory<Enemy>>();
}
```

## Injecting MonoBehaviours

A `MonoBehaviour` that wants services implements `IServiceResolverClient`. The scope's `IGameObjectInjector` calls
`Bind` on every such component under an object it injects: popups opened by the [UI router](../ui/router) and instances
rented from a [prefab pool](../systems/pooling) are injected this way. Each scope has its own injector.

```csharp compile
public sealed class EnemyView : MonoBehaviour, IServiceResolverClient
{
    private IDamageCalculator _calculator;

    public void Bind(IServiceResolver resolver) => _calculator = resolver.Resolve<IDamageCalculator>();

    public int Preview(int attack, int defence) => _calculator.Calculate(attack, defence);
}
```

With VContainer, your own views can keep using `[Inject]`; `IServiceResolverClient` is what the package's components
(`FoundationButton`, `LocalizedText`, ...) use so that they depend on no container.

## Writing another adapter

A Zenject, Reflex or Microsoft.Extensions.DependencyInjection adapter implements `IServiceRegistry`, `IServiceScope`,
`IGameObjectInjector` and `IGameContextScopeFactory`, and forwards the three entry-point interfaces.
`MinimalServiceScope` is a small in-memory implementation for tests and tools. It supports the three entry-point
interfaces, constructor defaults and `IEnumerable<T>`/`IReadOnlyList<T>`/`IReadOnlyCollection<T>`/array collections,
including parent registrations. Async entry points receive the scope lifetime token; the host drives `Tick()`. It has no
cycle detection, generated factories or GameObject injection; a shipping game should use a maintained container such as
the included VContainer adapter.

`MinimalServiceScope` disposes the singleton and scoped `IDisposable` instances it creates when the scope that owns them
is disposed. It does not track transients: a transient `IDisposable` is never disposed by the scope, and its creator
owns it. A transient that holds a resource should be disposed where it is created, or registered with a lifetime the
scope owns.

Collections are resolved, not cached. Each resolve of `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` or
`T[]` builds a new array and resolves every matching registration again. Singletons and scoped services come back as the
same instances; transients are new each time. Treat the array as a snapshot, not a live view.
