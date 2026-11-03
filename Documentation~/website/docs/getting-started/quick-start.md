---
title: Quick start
sidebar_position: 2
---

# Quick start

This page builds the smallest useful application: a VContainer root scope, one feature installed into it, and an entry
point that talks to the feature through commands, queries and events.

Prerequisites: the package, UniTask and VContainer are installed (see [Installation](installation)).

## 1. The root scope

A scene component derives from `VContainerRootScope<TEntryPoint>` and lists the project's feature installers.
`PrebindActions` registers what every feature expects to find, such as the message buses.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Infrastructure.VContainer;
using Foundation.Lifecycle;
using Foundation.Messaging;
using UnityEngine;

public sealed class GameScope : VContainerRootScope<GameEntryPoint>
{
    protected override IReadOnlyList<IFeatureInstaller> FeatureInstallers { get; } = new IFeatureInstaller[]
    {
        new GoldInstaller(),
    };

    protected override void PrebindActions(IServiceRegistry registry) => registry.RegisterMessaging();
}
```

Create a scene, add an empty GameObject and attach `GameScope` to it.

## 2. A feature

A feature is a set of types plus an installer. The installer receives an `IServiceRegistry`, so it never mentions
VContainer.

```csharp compile
public sealed class GoldWallet
{
    public int Total { get; private set; }

    public void Add(int amount) => Total += amount;
}

public readonly struct AddGoldCommand
{
    public AddGoldCommand(int amount) => Amount = amount;

    public int Amount { get; }
}

public readonly struct GetGoldQuery
{
}

public readonly struct GoldChangedEvent
{
    public GoldChangedEvent(int total) => Total = total;

    public int Total { get; }
}

public sealed class AddGoldCommandHandler : ICommandHandler<AddGoldCommand>
{
    private readonly GoldWallet _wallet;
    private readonly IEventBus _events;

    public AddGoldCommandHandler(GoldWallet wallet, IEventBus events)
    {
        _wallet = wallet;
        _events = events;
    }

    public async UniTask HandleAsync(AddGoldCommand command, CancellationToken cancellationToken = default)
    {
        _wallet.Add(command.Amount);
        await _events.PublishAsync(new GoldChangedEvent(_wallet.Total), cancellationToken);
    }
}

public sealed class GetGoldQueryHandler : IQueryHandler<GetGoldQuery, int>
{
    private readonly GoldWallet _wallet;

    public GetGoldQueryHandler(GoldWallet wallet) => _wallet = wallet;

    public UniTask<int> HandleAsync(GetGoldQuery query, CancellationToken cancellationToken = default)
        => UniTask.FromResult(_wallet.Total);
}

public sealed class GoldInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<GoldWallet>(ServiceLifetime.Singleton);
        registry.RegisterCommandHandler<AddGoldCommand, AddGoldCommandHandler>();
        registry.RegisterQueryHandler<GetGoldQuery, int, GetGoldQueryHandler>();
    }
}
```

## 3. The entry point

`RootEntryPoint` is started once the container is built. `RunAsync` is the application's startup sequence; in a real game
it ends by entering a [game context](../app/game-contexts).

```csharp compile
public sealed class GoldLogger : IEventHandler<GoldChangedEvent>
{
    public UniTask HandleAsync(GoldChangedEvent message, CancellationToken cancellationToken = default)
    {
        Debug.Log($"Gold changed: {message.Total}");
        return UniTask.CompletedTask;
    }
}

public sealed class GameEntryPoint : RootEntryPoint
{
    private readonly ICommandBus _commands;
    private readonly IQueryBus _queries;
    private readonly IEventBus _events;

    public GameEntryPoint(ICommandBus commands, IQueryBus queries, IEventBus events)
    {
        _commands = commands;
        _queries = queries;
        _events = events;
    }

    protected override async UniTask RunAsync(CancellationToken cancellation)
    {
        using var subscription = _events.Subscribe(new GoldLogger());

        await _commands.SendAsync(new AddGoldCommand(10), cancellation);
        var total = await _queries.QueryAsync<GetGoldQuery, int>(new GetGoldQuery(), cancellation);

        Debug.Log($"Gold: {total}");
    }
}
```

Press Play. The console shows `Gold changed: 10` and then `Gold: 10`.

## What just happened

- The scope built the container, applied `GoldInstaller`, and started `GameEntryPoint` as an async entry point.
- `RegisterCommandHandler` registered the handler and connected it to the command bus when the scope was built. The
  connection is removed when the scope is disposed.
- `GoldLogger` is subscribed by hand in `RunAsync` and unsubscribed when the `using` ends; a handler registered with the
  registry would be connected for the lifetime of the scope instead.
- The entry point depends on the buses, not on `GoldWallet`: callers go through messages.

## Where to go next

- Add persistence: [Save](../app/save).
- Run a game inside its own scope: [Game contexts](../app/game-contexts).
- Show a screen: [UI router](../ui/router).
- Put a loading screen in front of all of this: [Boot pipeline](../concepts/app-composition#1-boot-before-the-container-exists).
