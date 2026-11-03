---
title: Scripted sequences
sidebar_position: 3
---

# Scripted sequences

`Foundation.Sequences` runs linear scripted steps on UniTask: do this, wait for that, branch on a condition, race two
waits. It has no dependency on UI, messaging, DI or any content format. A consumer builds `SequenceNode` arrays (from a
parser, a ScriptableObject, or by hand) and asks the runner to execute them. The [tutorial](tutorial) is built on it.

| Primitive | Meaning |
| --- | --- |
| `action <key> ...` | Run the registered action handler. |
| `wait <key> ...` | Await the registered wait handler (usually "until this event happens"). |
| `wait_until [not] <key>` | Poll a condition on the player loop until it is true. |
| `skip_if [not] <key>` | If the condition holds, end the step normally. Cleanup still runs. |
| `delay <seconds>` | Wait in unscaled time. |
| `if` / `else` | Run the children or the else-children depending on a condition. |
| `race` | Run every child at once; the first to finish wins and the rest are canceled and drained. |

The primitives intentionally omit loops, functions, and an expression interpreter. Provide an `ISequenceCommandHandler`
for behavior beyond the primitives (for example `say` or `highlight` in a tutorial).

## Handlers

Handlers are registered under explicit string keys in a `SequenceHandlerRegistry`: actions and waits are async
delegates, conditions are fast synchronous predicates. A duplicate key within a category fails at registration. A
feature can also expose its handlers as an `ISequenceHandlerProvider`, which lets a hot-updated module contribute
handlers without any assembly scanning.

```csharp compile
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using Foundation.Sequences;

public readonly struct DoorOpenedEvent
{
}

public sealed class DoorSequenceHandlers : ISequenceHandlerProvider
{
    private readonly IEventBus _events;
    private bool _doorIsOpen;

    public DoorSequenceHandlers(IEventBus events) => _events = events;

    public void Register(SequenceHandlerRegistry registry)
    {
        registry.RegisterAction("log", (context, _) =>
        {
            UnityEngine.Debug.Log($"{context.SequenceId}/{context.StepId}: {string.Join(" ", context.Arguments)}");
            return UniTask.CompletedTask;
        });

        registry.RegisterWait("door_opened", async (_, cancellationToken) =>
        {
            await _events.WaitForEventAsync<DoorOpenedEvent>(null, cancellationToken);
            _doorIsOpen = true;
        });

        registry.RegisterCondition("door_is_open", _ => _doorIsOpen);
    }
}
```

`context.Arguments` holds the node's arguments after the handler key.

## Running a step

`SequenceRunner.RunStepAsync(sequenceId, stepId, stepIndex, body, cleanup, cancellationToken)` runs a body, then the
cleanup nodes, even if the body threw, was canceled or ended early. Cleanup runs with `CancellationToken.None`, so keep
it finite and never wait for player input in it.

If the body fails and cleanup succeeds, the body exception is rethrown with its original stack trace. If both fail, the
runner throws an `AggregateException` containing both exceptions; a cleanup-only failure propagates on its own.

```csharp compile
public sealed class DoorTour
{
    private readonly SequenceRunner _runner;

    public DoorTour(SequenceHandlerRegistry handlers) => _runner = new SequenceRunner(handlers);

    public UniTask RunAsync(CancellationToken cancellationToken)
    {
        var body = new[]
        {
            SequenceNode.Action("log", "waiting for the door"),
            new SequenceNode { Command = "skip_if", Arguments = new[] { "door_is_open" } },
            new SequenceNode { Command = "wait", Arguments = new[] { "door_opened" } },
            new SequenceNode { Command = "delay", Arguments = new[] { "0.5" } },
            SequenceNode.Action("log", "door opened"),
        };

        var cleanup = new[] { SequenceNode.Action("log", "tour finished") };

        return _runner.RunStepAsync("door_tour", "wait_for_door", 0, body, cleanup, cancellationToken);
    }
}
```

A custom command (anything that is not `action`, `wait`, `wait_until`, `skip_if` or `delay`) is passed to the
`ISequenceCommandHandler` given to the runner. Without one, an unknown command throws with its source line.

## Branching and racing

`If` and `Race` are node kinds. An `If` node's `Command` is the condition key (set `Negated` for "not"), `Children` run
when the condition holds and `ElseChildren` when it does not. A `Race` node starts each child concurrently and finishes
when the first one does; the losers are canceled and awaited before the step moves on, so handlers must honor
cancellation.

```csharp compile
public static class DoorBranches
{
    public static SequenceNode[] WaitForDoorOrTimeout() => new[]
    {
        new SequenceNode
        {
            Kind = SequenceNodeKind.If,
            Command = "door_is_open",
            Negated = true,
            Children = new[]
            {
                new SequenceNode
                {
                    Kind = SequenceNodeKind.Race,
                    Children = new[]
                    {
                        new SequenceNode { Command = "wait", Arguments = new[] { "door_opened" } },
                        new SequenceNode { Command = "delay", Arguments = new[] { "10" } },
                    },
                },
            },
        },
    };
}
```

## What the runner does not do

It does not own checkpoints, save state, eligibility, analytics, asset instances, UI focus or dialogue. The tutorial
layer adds chapters, a parser, a validator, persistence and presentation on top; other consumers can feed nodes in
directly without depending on it.
