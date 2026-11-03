---
title: VContainer
sidebar_position: 2
---

# VContainer

`Foundation.Infrastructure.VContainer` is the reference implementation of the [DI port](../core/di). It compiles
automatically when `jp.hadashikick.vcontainer` and UniTask are installed. Everything in Foundation registers through
`Foundation.DI` (`IServiceRegistry`, `IFeatureInstaller`); this assembly maps that onto VContainer, and nothing else in
the package references VContainer.

| Type | Role |
| --- | --- |
| `VContainerRootScope<TEntryPoint>` | A `LifetimeScope` that applies your installers and starts a `RootEntryPoint`. |
| `VContainerServiceRegistry` | Wraps an `IContainerBuilder`. Also makes `IServiceResolver`, `IServiceScope` and `IGameObjectInjector` resolvable in every scope that uses it, and forwards the entry-point interfaces to VContainer's `IStartable`/`IAsyncStartable`/`ITickable` dispatch. |
| `VContainerServiceScope` | Wraps an `IObjectResolver`. `CreateScope` makes a child scope that owns and disposes its container. |
| `VContainerGameObjectInjector` | Calls VContainer's `InjectGameObject`, then `IServiceResolverClient.Bind` for the package's own components (`FoundationButton`, `LocalizedText`, ...). Your own views keep using `[Inject]`. |
| `VContainerGameContextScopeFactory` | The child scope of a [game context](../app/game-contexts) (`IGameContextScopeFactory`). |
| `FoundationContainerBuilderExtensions` | `builder.AsServiceRegistry()`, `resolver.AsServiceScope()`, `installer.Install(builder)`, and `RegisterMessaging`, `RegisterCommandHandler`, `RegisterQueryHandler`, `RegisterScheduler` directly on an `IContainerBuilder`. |
| `VContainerRegistryExtensions` | `registry.ToContainerBuilder()` returns the underlying `IContainerBuilder`. |

## A root scope

`FeatureInstallers` lists the installers the scope applies in order. `PrebindActions` runs first, for what the
installers expect to find already registered (the message buses, the scheduler, the game-context scope factory) and for
scene references the container cannot create.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Audio;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.Infrastructure.VContainer;
using Foundation.Lifecycle;
using Foundation.Messaging;
using Foundation.Pooling;
using Foundation.Scheduling;

public sealed class GameEntryPoint : RootEntryPoint
{
    private readonly IGameContextController _contexts;

    public GameEntryPoint(IGameContextController contexts) => _contexts = contexts;

    protected override async UniTask RunAsync(CancellationToken cancellation)
        => await _contexts.EnterStartupContextAsync(cancellation);
}

public sealed class GameScope : VContainerRootScope<GameEntryPoint>
{
    protected override IReadOnlyList<IFeatureInstaller> FeatureInstallers { get; } = new IFeatureInstaller[]
    {
        new PoolingInstaller(),
        new AudioInstaller(new IAudioChannelDefinition[] { new AudioChannelDefinition("ui") }),
    };

    protected override void PrebindActions(IServiceRegistry registry)
    {
        registry.RegisterMessaging();
        registry.RegisterScheduler();
        registry.Register<VContainerGameContextScopeFactory>(ServiceLifetime.Singleton).As<IGameContextScopeFactory>();
    }
}
```

`VContainerGameContextScopeFactory` takes VContainer's own `IObjectResolver`, which VContainer always provides.
The project must also register an `ITimeProvider` for the scheduler and an `IGameContextController` with its
dependencies for the entry point; those registrations are not shown here.

## Mixing with plain VContainer

Code that already has an `IContainerBuilder` can use Foundation installers without a root scope:
`installer.Install(builder)` applies an `IFeatureInstaller`, and `builder.AsServiceRegistry()` gives the
`IServiceRegistry` view of the same builder. In the other direction, an installer that needs a VContainer-only feature
(parameters, components, factories) can take the builder back with `registry.ToContainerBuilder()`. Doing so ties that
installer to VContainer; installers that stay on `IServiceRegistry` do not need it.

## Swapping the container

To use another DI library, write an adapter ([how](overview#a-di-adapter-concretely)).
