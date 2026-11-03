---
title: Messaging
sidebar_position: 2
---

# Messaging

Three buses, so gameplay code depends on `ICommandBus`, `IQueryBus` and `IEventBus` instead of a messaging library.

| | Meaning | Handlers | Contract |
| --- | --- | --- | --- |
| **Command** | "Do this." Mutates state. | exactly one | `ICommandBus.SendAsync<T>`, `ICommandHandler<T>` |
| **Query** | "Compute and return this." | exactly one | `IQueryBus.QueryAsync<TQuery, TResult>`, `IQueryHandler<TQuery, TResult>` |
| **Event** | "This happened." A past-tense fact. | any number, including zero | `IEventBus.PublishAsync<T>`, `IEventBus.Subscribe<T>`, `IEventHandler<T>` |

A command or query without a handler fails the call instead of waiting or being dropped. Events cannot cancel a
mutation; publish them after the state has changed. An ordinary handler failure does not prevent the bus from invoking
the other handlers, and once the publish finishes without cancellation, their failures are reported together.
Cancellation is cooperative: both modes check the token before starting the next handler, so a cancelled publish starts
no further handlers. In `Sequential` mode the publish throws `OperationCanceledException` if a handler obeys the token
or cancellation is detected before the next handler. If the last handler ignores cancellation, the publish may complete
normally. A publish with no matching subscribers also completes without checking the token. In `Concurrent` mode handlers may already have started, and the
publish throws at once without waiting for them; each running handler must observe the token itself, and cancellation
cannot undo work already done. Failures raised before or after a cancellation go to
`UniTaskScheduler.UnobservedTaskException` instead of the cancelled result. `WaitForEventAsync` cancels its wait and
removes its subscription.

## Registering

The application scope registers the buses once. Handlers are registered by the scope that owns them and leave the bus
when that scope is disposed, so a game context can be left and entered again.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Messaging;

public readonly struct PurchaseUpgradeCommand
{
    public PurchaseUpgradeCommand(string upgradeId) => UpgradeId = upgradeId;

    public string UpgradeId { get; }
}

public readonly struct CanAffordQuery
{
    public CanAffordQuery(int cost) => Cost = cost;

    public int Cost { get; }
}

public readonly struct UpgradePurchasedEvent
{
    public UpgradePurchasedEvent(string upgradeId) => UpgradeId = upgradeId;

    public string UpgradeId { get; }
}

public sealed class PurchaseUpgradeCommandHandler : ICommandHandler<PurchaseUpgradeCommand>
{
    private readonly IEventBus _events;

    public PurchaseUpgradeCommandHandler(IEventBus events) => _events = events;

    public async UniTask HandleAsync(PurchaseUpgradeCommand command, CancellationToken cancellationToken = default)
    {
        // Validate, spend, apply...
        await _events.PublishAsync(new UpgradePurchasedEvent(command.UpgradeId), cancellationToken);
    }
}

public sealed class CanAffordQueryHandler : IQueryHandler<CanAffordQuery, bool>
{
    public UniTask<bool> HandleAsync(CanAffordQuery query, CancellationToken cancellationToken = default)
        => UniTask.FromResult(query.Cost <= 100);
}

public sealed class UpgradesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterCommandHandler<PurchaseUpgradeCommand, PurchaseUpgradeCommandHandler>();
        registry.RegisterQueryHandler<CanAffordQuery, bool, CanAffordQueryHandler>();
    }
}
```

In the application scope:

```csharp
registry.RegisterMessaging();                          // CommandBus, QueryBus and the in-process EventBus
registry.RegisterMessaging(new MessagingOptions(...)); // the same, with the ordering chosen (see below)
```

`RegisterMessaging()` registers `ICommandBus`, `IQueryBus`, `IEventBus` and the two handler registries
(`ICommandHandlerRegistry`, `IQueryHandlerRegistry`). `RegisterCommandAndQueryBuses()` registers only the command and
query buses, for a project that brings its own `IEventBus`.

## Ordering

What order handlers and commands run in is a setting of the registration, `MessagingOptions`, not a property of the
library underneath:

| Setting | Value | What it guarantees |
| --- | --- | --- |
| `Events` | `Sequential` (default) | Each handler finishes before the next starts, so a later handler sees what an earlier one did. A handler that unsubscribes while an earlier one is still running is not called. A cancelled publish does not start the next handler. |
| | `Concurrent` | Handlers start without waiting for earlier handlers to finish, and the publish waits for all of them unless it is cancelled. Completion order is not guaranteed. |
| `Commands` | `Immediate` (default) | A command goes to its handler at once; commands sent meanwhile may overlap. |
| | `Serialized` | One command at a time, in the order they were sent: a command waits until the one before it has finished. |

```csharp
// Process event handlers sequentially and state-changing commands one after another.
registry.RegisterMessaging(new MessagingOptions(EventDispatchMode.Sequential, CommandExecutionMode.Serialized));
```

Subscriptions for the same event type run in subscription order. A published type can match subscriptions to several
base types or interfaces; the bus groups these by subscribed type and does not guarantee a global subscription order.

With `Serialized`, a command that is waiting can be canceled and then never runs; a command that fails hands the turn to
the next one. A handler must not `await` another command sent through the same bus: that command would wait for the
handler that is awaiting it. Send it without awaiting, or call the other feature's service directly. Queries are
read-only and are not queued.

A failing event handler does not keep the later ones from receiving the event. When the publish finishes without
cancellation, the failures are reported together: a single failure is rethrown with its original stack, and several
arrive as an `AggregateException`.

## Using the buses

```csharp compile
public sealed class UpgradeButtonPresenter
{
    private readonly ICommandBus _commands;
    private readonly IQueryBus _queries;

    public UpgradeButtonPresenter(ICommandBus commands, IQueryBus queries)
    {
        _commands = commands;
        _queries = queries;
    }

    public async UniTask<bool> TryBuyAsync(string upgradeId, int cost, CancellationToken cancellationToken)
    {
        var canAfford = await _queries.QueryAsync<CanAffordQuery, bool>(new CanAffordQuery(cost), cancellationToken);
        if (!canAfford)
            return false;

        await _commands.SendAsync(new PurchaseUpgradeCommand(upgradeId), cancellationToken);
        return true;
    }
}
```

The bus looks the handler up by the static type of the message, never by its runtime type, so a command is never
mis-routed.

## Subscribing to events

`Subscribe` returns an `IDisposable`; dispose it to unsubscribe.

A publish uses the handler snapshot taken when it starts. A subscription added during a publish, or an unsubscribe in
`Concurrent` mode, applies to the next publish; in `Sequential` mode a handler unsubscribed before its turn is skipped.
The bus caches one snapshot per published event type, keeps at most 64 of them, and clears the cache when a subscription
changes or the limit is reached.

```csharp compile
public sealed class UpgradeToast : IEventHandler<UpgradePurchasedEvent>
{
    public UniTask HandleAsync(UpgradePurchasedEvent message, CancellationToken cancellationToken = default)
    {
        // Show "upgrade bought".
        return UniTask.CompletedTask;
    }
}

public sealed class UpgradeToastController : System.IDisposable
{
    private readonly System.IDisposable _subscription;

    public UpgradeToastController(IEventBus events) => _subscription = events.Subscribe(new UpgradeToast());

    public void Dispose() => _subscription.Dispose();
}
```

### Waiting for one event

`WaitForEventAsync` is a one-shot wait that subscribes and unsubscribes for you, with an optional predicate:

```csharp compile
public sealed class UpgradeWaiter
{
    private readonly IEventBus _events;

    public UpgradeWaiter(IEventBus events) => _events = events;

    public UniTask<UpgradePurchasedEvent> NextPurchaseOfAsync(string upgradeId, CancellationToken cancellationToken)
        => _events.WaitForEventAsync<UpgradePurchasedEvent>(e => e.UpgradeId == upgradeId, cancellationToken);
}
```

### Base types and interfaces

`EventBus` delivers an event to every subscriber whose event type is assignable from the type it was published as. A
subscriber to `IEventHandler<IGameplayEvent>` therefore receives every event published as a type implementing
`IGameplayEvent`. The type is the static `TEvent` of the `PublishAsync<TEvent>` call.

## Registering by hand

`ICommandHandlerRegistry.RegisterHandler<T>` and `IQueryHandlerRegistry.RegisterHandler<TQuery, TResult>` return an
`IDisposable` for code that registers handlers outside an installer. Disposing it removes the handler.

## Event markers

`IEventMarker` and its three sub-interfaces (`IGameplayEvent`, `IAnalyticsEvent`, `IApplicationEvent`) let a subscriber
or a tool filter events by kind. `IAnalyticsEvent` additionally carries a `Name` and `Parameters`. Using the markers is
optional.

## Tracing

In the Editor and in players built with `FOUNDATION_DEVELOPMENT`, `TracingCommandBus`, `TracingQueryBus` and
`TracingEventBus` decorate a bus and report each dispatch to an `IOperationTracer` (see
[Diagnostics](diagnostics#operation-tracing)). You compose the decorators yourself when you register the buses; they are
omitted from production players.

## Conventions

- A command is named in the imperative (`PurchaseUpgradeCommand`), an event in the past tense
  (`UpgradePurchasedEvent`).
- Not every method call needs a command. Use one where a request crosses a feature boundary, where tracing it helps, or
  where the sender must not know the handler.
- A UI should read state through queries or read models, not through another feature's runtime state.
- Do not model `BeforeX` events: since events cannot cancel a mutation, a "before" event only adds ordering bugs. Validate
  in the command handler instead.
