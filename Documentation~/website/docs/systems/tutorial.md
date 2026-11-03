---
title: Tutorial
sidebar_position: 4
---

# Tutorial

This is a chapter-based onboarding system. A **chapter** has a trigger, an optional requirement and an ordered list of steps.
`TutorialDirector` listens for triggers and starts the next eligible chapter; progress is saved as its own
[save](../app/save) segment, and a chapter that was interrupted resumes after its last checkpoint.

| Type | Role |
| --- | --- |
| `TutorialChapterDefinition` | A chapter: id, trigger, steps, checkpoints, priority, requirement. |
| `ITutorialChapterSource` / `TutorialChapterCatalog` | Where chapters come from, and the ordered set of them. |
| `ITutorialStep` | One step. Registered in a `TutorialRegistry` by id. |
| `TutorialService` | Runs one chapter's steps in order. Chapters never run in parallel. |
| `TutorialDirector` | Maps triggers to chapters, checks requirements, runs the next eligible chapter. |
| `TutorialProgressState` / `TutorialSaveModule` | Per-chapter status and its persistence. |
| `ITutorialTrigger` | Raises trigger ids by itself (a screen opened, a threshold reached). |
| `ITutorialPresentationGate` | Suppresses starting chapters while something else owns the screen. |
| `TutorialChapterCompletedRequirement` | An `IRequirement` that holds once a chapter is completed. |

The module has no serializer dependency: `TutorialConfig` and `TutorialSnapshot` are plain classes serialized by the
[`ISerializer`](../adapters/serialization) implementation registered by the project.

## Steps and chapters in C#

The smallest tutorial is a step class and a chapter that names it. `TutorialStepId`, `TutorialChapterId` and
`TutorialTriggerId` are constructed from strings.

```csharp compile
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Requirements;
using Foundation.Save;
using Foundation.Tutorial;

public sealed class WelcomeStep : ITutorialStep
{
    public TutorialStepId Id { get; } = new TutorialStepId("welcome.intro");

    public async UniTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        UnityEngine.Debug.Log("Welcome!");
        await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
    }
}

public sealed class TipStep : ITutorialStep
{
    public TutorialStepId Id { get; } = new TutorialStepId("welcome.tip");

    public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        UnityEngine.Debug.Log("Tap the building to collect.");
        return UniTask.CompletedTask;
    }
}

public sealed class FixedChapters : ITutorialChapterSource
{
    public static readonly TutorialTriggerId HomeOpened = new TutorialTriggerId("home-opened");

    public UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(CancellationToken cancellationToken = default)
    {
        var chapter = new TutorialChapterDefinition(
            new TutorialChapterId("welcome"),
            HomeOpened,
            new[] { new TutorialStepId("welcome.intro"), new TutorialStepId("welcome.tip") },
            checkpoints: new[] { new TutorialStepId("welcome.intro") });

        return UniTask.FromResult<IReadOnlyList<TutorialChapterDefinition>>(new[] { chapter });
    }
}
```

A **checkpoint** is a step after which progress is saved: a resumed chapter restarts from the step after the last
completed checkpoint instead of from the beginning.

In a data-driven setup the chapters come from configs instead: `ConfigTutorialChapterSource` reads `TutorialConfig`
(`Id`, `TriggerId`, `Priority`, `StepIds`, `CheckpointStepIds`) through the [config resolver](../app/config), and takes
an `ITutorialChapterRequirements` to attach a requirement to each chapter.

## Wiring

The system provides no all-in-one installer: its pieces are plain classes, so register them in your own feature
installer. Factory registrations avoid constructor ambiguity for classes with optional collection parameters.

```csharp compile
public sealed class TutorialInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<FixedChapters>(ServiceLifetime.Singleton).As<ITutorialChapterSource>();
        registry.Register<TutorialChapterCatalog>(ServiceLifetime.Singleton);
        registry.Register<TutorialProgressState>(ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialRegistry(new ITutorialStep[] { new WelcomeStep(), new TipStep() }),
            ServiceLifetime.Singleton);

        registry.Register<TutorialService>(ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialDirector(
                resolver.Resolve<TutorialChapterCatalog>(),
                resolver.Resolve<TutorialService>(),
                resolver.Resolve<TutorialProgressState>()),
            ServiceLifetime.Singleton);

        registry.Register<TutorialSaveModule>(ServiceLifetime.Singleton).As<IFeatureSaveModule>();
    }
}
```

After the save is loaded, initialize the director once and resume whatever was interrupted. From then on, anything can
raise a trigger:

```csharp compile
public sealed class TutorialBootstrap
{
    private readonly TutorialDirector _director;

    public TutorialBootstrap(TutorialDirector director) => _director = director;

    public async UniTask StartAsync(CancellationToken cancellationToken)
    {
        await _director.InitializeAsync(cancellationToken);
        await _director.ResumeAsync(cancellationToken);
    }

    public void OnHomeScreenOpened() => _director.Raise(FixedChapters.HomeOpened);

    public UniTask OnHomeScreenOpenedAndWaitAsync(CancellationToken cancellationToken)
        => _director.RaiseAndWaitAsync(FixedChapters.HomeOpened, cancellationToken);
}
```

`Raise` is fire-and-forget. `RaiseAsync` returns when the start attempt has finished. `RaiseAndWaitAsync` also waits
until no chapter is running, for a screen that must let the tutorial finish speaking before it moves on.
`WaitForIdleAsync` does only the waiting.

A trigger only means "evaluate again": a chapter blocked by a requirement or by another running chapter starts the next
time something raises a trigger or calls `TryStartNextAsync`.

## Requirements

A chapter's requirement is any [`IRequirement`](../core/requirements-rewards). A chapter can depend on another with
`TutorialChapterCompletedRequirement`, which also composes with `Requirement.All/Any/Not` and the rest of your gating:

```csharp compile
public sealed class AfterWelcome : ITutorialChapterRequirements
{
    private readonly TutorialProgressState _state;

    public AfterWelcome(TutorialProgressState state) => _state = state;

    public IRequirement GetRequirement(TutorialChapterId chapterId)
        => chapterId.Value == "shop"
            ? new TutorialChapterCompletedRequirement(_state, new TutorialChapterId("welcome"))
            : null;
}
```

## Triggers and gates

An `ITutorialTrigger` raises trigger ids on its own once the director activates it with a sink. Pass triggers (and any
`ITutorialPresentationGate`, whose `IsSuppressed` blocks starting a chapter) to the `TutorialDirector` constructor.

```csharp compile
public sealed class LevelReachedTrigger : ITutorialTrigger
{
    private readonly IEventBus _events;
    private IDisposable _subscription;

    public LevelReachedTrigger(IEventBus events) => _events = events;

    public TutorialTriggerId Id { get; } = new TutorialTriggerId("level-3");

    public void Activate(ITutorialTriggerSink sink)
        => _subscription = _events.Subscribe(new Handler(sink, Id));

    public void Dispose() => _subscription?.Dispose();

    private sealed class Handler : IEventHandler<LevelReachedEvent>
    {
        private readonly ITutorialTriggerSink _sink;
        private readonly TutorialTriggerId _id;

        public Handler(ITutorialTriggerSink sink, TutorialTriggerId id)
        {
            _sink = sink;
            _id = id;
        }

        public UniTask HandleAsync(LevelReachedEvent message, CancellationToken cancellationToken = default)
        {
            if (message.Level >= 3)
                _sink.Raise(_id);

            return UniTask.CompletedTask;
        }
    }
}

public readonly struct LevelReachedEvent
{
    public LevelReachedEvent(int level) => Level = level;

    public int Level { get; }
}
```

## Writing chapters as text: Flow

Writing a C# class for every "say this, point at that, wait for the tap" step gets tedious. `Foundation.Tutorial.Flow`
compiles a small text DSL into chapters and runs it on [scripted sequences](sequences). It also has no serializer
dependency.

The `Foundation.Tutorial.Flow` assembly is not auto-referenced: add it to the `references` of your own assembly
definition to use these types.

One file per chapter:

```text
# comment
chapter tutorial.welcome            # unique chapter id
trigger home-opened                 # trigger id that starts it
priority 0                          # optional; higher runs first when several are ready
checkpoints all                     # optional: all | none (default none)

step tutorial.welcome.intro [checkpoint]
  say tutorial.welcome.hello speaker=tutorial.speaker.guide
  highlight shop.button hand=on
  wait_click shop.button
  finally
    close
end
```

- Step ids are save and analytics ids: never rename the steps of a shipped chapter.
- The last step must leave the overlay closed on every path: put `close` after any later `say`, `highlight` or `show`, or
  in `finally` after a `skip_if`. The validator checks the command order and early exits.
- Quote arguments that contain spaces: `say "some key"`.
- `if [not] <condition> ... [else ...] end` and `race ... end` (each direct child of a `race` is a branch; the first to
  finish wins, the others are canceled).

### Primitives

| Command | Meaning |
| --- | --- |
| `say <text-key> [continue=tap\|none] [target=<id>] [side=auto\|above\|below\|left\|right] [speaker=<text-key>] [arrow=on\|off]` | Narrator popup. `tap` (default) waits for the player. Without `target`, the request asks the presenter to follow the highlight; the presenter decides where to place it when nothing is highlighted. |
| `highlight <target-id> [padding=<px>] [hand=on\|off] [timeout=<s>]` | Spotlight a target. Waits up to `timeout` (default 10 s) for the target to become available, then continues and logs a warning if a logger is configured. |
| `clear_highlight` | Remove the spotlight. Focus is also cleared when a step ends. |
| `show` / `hide` | Show or hide the overlay without closing it. |
| `close` | Close the overlay. The next `say`, `highlight` or `show` reopens it. |
| `wait <wait-key> args...` | Wait for a game event (`ITutorialWait`). |
| `wait_until [not] <condition> args...` | Wait until a condition holds (polled per frame). |
| `wait_target <target-id> [timeout=<s>]` | Wait for an available target, up to `timeout` (default 10 s). Availability does not guarantee that the target is visible on screen. |
| `wait_click <target-id> [timeout=<s>]` | Wait until the player clicks the target. If the target is unavailable for `timeout` (default 10 s), continue and log a warning if a logger is configured. The timeout does not limit the wait for a click. |
| `delay <seconds>` | Unscaled delay. |
| `action <action-key> args...` | Run a game action (`ITutorialAction`). |
| `skip_if [not] <condition> args...` | End the step now; its `finally` still runs. |

Text keys are [localization](../app/localization) keys.

### Game commands

`action`, `wait` and conditions call into your game through three small interfaces. A class declares its key and
parameters with `[TutorialCommand]`; a `?` suffix marks an optional parameter. The editor validator reads those
declarations, so a script that calls an unknown command or passes too few arguments fails the config build.

```csharp compile
using Foundation.Tutorial.Flow;

[TutorialCommand("wallet.give", "amount")]
public sealed class GiveCoinsAction : ITutorialAction
{
    private readonly ICommandBus _commands;

    public GiveCoinsAction(ICommandBus commands) => _commands = commands;

    public UniTask ExecuteAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        => _commands.SendAsync(new GiveCoinsCommand(context.IntArgument(0)), cancellationToken);
}

[TutorialCommand("shop.opened")]
public sealed class ShopOpenedWait : ITutorialWait
{
    private readonly IEventBus _events;

    public ShopOpenedWait(IEventBus events) => _events = events;

    public UniTask WaitAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        => _events.WaitForEventAsync<ShopOpenedEvent>(null, cancellationToken);
}

[TutorialCommand("wallet.has_at_least", "amount")]
public sealed class HasCoinsCondition : ITutorialCondition
{
    private readonly ICoinWallet _wallet;

    public HasCoinsCondition(ICoinWallet wallet) => _wallet = wallet;

    public bool Evaluate(TutorialCommandContext context) => _wallet.Coins >= context.IntArgument(0);
}

public interface ICoinWallet
{
    int Coins { get; }
}

public readonly struct GiveCoinsCommand
{
    public GiveCoinsCommand(int amount) => Amount = amount;

    public int Amount { get; }
}

public readonly struct ShopOpenedEvent
{
}
```

In the script: `action wallet.give 50`, `wait shop.opened`, `skip_if wallet.has_at_least 500`.

### Targets

A target is a stable id on something the tutorial can point at. Put a `TutorialTargetHost` on the UI root and register
its `Registry` as `ITutorialTargetRegistry`; views then mark objects with a `TutorialTarget` component in the prefab or
from code:

```csharp compile
using Foundation.UI;
using UnityEngine;

public sealed class ShopButtonView : MonoBehaviour
{
    private void Awake() => TutorialTarget.Attach(gameObject, "shop.button");
}
```

A target is available while it is active, and `wait_click` / `highlight` find it by id, so an object that appears later
(a popup, a list row) can be pointed at as soon as it exists.

### Presentation

The project provides what the player sees by implementing three contracts, usually as one presenter over a tutorial
panel. Nothing about fonts, animation or prefabs is in Foundation.

| Contract | Used by | Purpose |
| --- | --- | --- |
| `ITutorialOverlay` | `show`, `hide`, `close` | The dim and input-blocking layer. `OpenAsync` returns false if it cannot open. |
| `ITutorialDialoguePresenter` | `say` | Shows a `TutorialDialogueRequest` (text key, speaker, anchor, `TooltipSide`, arrow) and completes when the player continues. |
| `ITutorialFocusPresenter` | `highlight`, `clear_highlight` | Cuts a spotlight around a `TutorialFocusRequest` target. |

The dialogue's side is a `TooltipSide`, the same type [`TooltipBubble`](../ui/router#tooltips) uses to stand beside a
target, so one bubble can serve both.

```csharp compile
public sealed class TutorialPanel : ITutorialOverlay, ITutorialDialoguePresenter, ITutorialFocusPresenter
{
    private readonly GameObject _root;

    public TutorialPanel(GameObject root) => _root = root;

    public ITutorialTarget Focused { get; private set; }

    public UniTask<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _root.SetActive(true);
        return UniTask.FromResult(true);
    }

    public void SetVisible(bool visible) => _root.SetActive(visible);

    public void Close()
    {
        Focused = null;
        _root.SetActive(false);
    }

    public async UniTask ShowAsync(TutorialDialogueRequest request, CancellationToken cancellationToken = default)
    {
        // Localize request.TextKey, anchor the bubble to request.Anchor on request.Side...
        if (request.WaitForContinue)
            await UniTask.WaitUntil(() => UnityEngine.Input.anyKeyDown, cancellationToken: cancellationToken);
    }

    public void Focus(TutorialFocusRequest request) => Focused = request.Target;

    public void ClearFocus() => Focused = null;
}
```

### Putting Flow together

Register the three presentation contracts, the target registry and the command classes, then build the runner and use
`ScriptTutorialChapterSource` as the chapter source in place of the C# one above:

```csharp compile
using Foundation.Messaging;

public sealed class FlowTutorialInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<GiveCoinsAction>(ServiceLifetime.Singleton).As<ITutorialAction>();
        registry.Register<ShopOpenedWait>(ServiceLifetime.Singleton).As<ITutorialWait>();
        registry.Register<HasCoinsCondition>(ServiceLifetime.Singleton).As<ITutorialCondition>();

        registry.Register(
            resolver => new TutorialCommandRegistry(
                resolver.Resolve<IReadOnlyList<ITutorialAction>>(),
                resolver.Resolve<IReadOnlyList<ITutorialCondition>>(),
                resolver.Resolve<IReadOnlyList<ITutorialWait>>()),
            ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialScriptRunner(
                resolver.Resolve<ITutorialOverlay>(),
                resolver.Resolve<ITutorialDialoguePresenter>(),
                resolver.Resolve<ITutorialFocusPresenter>(),
                resolver.Resolve<ITutorialTargetRegistry>(),
                resolver.Resolve<TutorialCommandRegistry>()),
            ServiceLifetime.Singleton);

        registry.Register<ScriptTutorialChapterSource>(ServiceLifetime.Singleton).As<ITutorialChapterSource>();
    }
}
```

`ITutorialStepObserver` (passed to the runner) hears every step start and completion, which is where tutorial analytics
go.

### Building the config

The chapter files are compiled into one config group by the editor's config pipeline (see [Config](../app/config)):
`TutorialScriptSourceProvider(folder, fileName, localizationCsvPath)` reads every `*.txt` in a folder (one chapter per
file), parses and validates it against the `[TutorialCommand]` types and, if a CSV path is given, against the
localization keys; `TutorialScriptExporter` writes all chapters into one `TutorialScriptConfig[]` group;
`TutorialScriptConfigValidator` reports parse and validation errors with file and line, and chapter or step ids declared
in more than one file. Any error fails the build.

```csharp
registry.RegisterSourceProvider(new TutorialScriptSourceProvider(
    "Assets/Content/Tutorials", "tutorial_scripts.bytes", localizationSettings.CsvPath));
registry.RegisterExporter(new TutorialScriptExporter());
registry.RegisterValidator(new TutorialScriptConfigValidator());
```

Delivering the exported file (Addressables address and group) is the project's job.

A chapter that fails three consecutive times is not retried for the lifetime of that `TutorialDirector`, including later
triggers and Resume calls. Other eligible chapters can still run. Recreating the director starts a new retry session.
