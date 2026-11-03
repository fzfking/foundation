---
title: uGUI components
sidebar_position: 2
---

# uGUI components

`Foundation.UI.UGui` contains reusable uGUI presentation components. It is a separate assembly that uses Foundation's
own reactive types; it has no dependency on a third-party text renderer, DI container, reactive library or tween library.

## FoundationButton

A drop-in replacement for `UnityEngine.UI.Button` with:

- inspector-authored string **states** (`normal`, `locked`, `busy`, `selected`, or project-specific ids);
- per-state input gate, localization key and arguments, state strategies, and enter and exit `UnityEvent`s;
- a `Clicked` event stream, plus `State` and `Busy` reactive values ([Reactive values](../core/reactive-motion));
- `SubscribeAsync`, which receives a cancellation token and **keeps the button locked until every registered async handler
  completes**;
- per-state **strategies** that can be combined and authored as ScriptableObjects or scene components;
- semantic `Signals` for input, selection, long press and invalid click attempts.

Subscribe clicks from code, per state or for all states:

```csharp compile
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using Foundation.UI.UGui;
using UnityEngine;

public readonly struct StartRunCommand
{
}

public sealed class PlayButtonPresenter : IDisposable
{
    private readonly IDisposable _click;

    public PlayButtonPresenter(FoundationButton button, ICommandBus commands)
    {
        // The button stays locked until the command completes, so a double tap cannot send it twice.
        _click = button.SubscribeAsync(
            "normal",
            (click, cancellationToken) => commands.SendAsync(new StartRunCommand(), cancellationToken));
    }

    public void Dispose() => _click.Dispose();
}

public static class ButtonStateExample
{
    public static void Lock(FoundationButton button)
    {
        if (button.HasState("locked"))
            button.SetState("locked");
    }
}
```

Use `Clicked` (or `Subscribe(stateId, handler)`) for observers that do not own completion. The button never assumes that an
arbitrary subscriber is asynchronous, so a client that needs locking must use `SubscribeAsync`.

### State strategies

A strategy reacts to a state change. Create reusable strategy assets from **Foundation → UI → Button State Strategies** and
combine them in a state. Built in: color change, scale, sprite loading through `IAssetProvider`, and switching active child
variants. A strategy receives its runtime services through `FoundationButtonStrategyContext`; callers write no service
boilerplate.

The color and scale strategies tween over their `duration` (0 applies at once) and do not hold up the strategies after
them. The scale is a multiple of the button's own size, kept as a `ScaleContribution`: a press punch, a selected tab and any
other effect on the same transform multiply together and take themselves off again, so overlapping effects return to
the original size as long as no other code assigns `Transform.localScale` directly while a contribution is active. Use a
`ScaleContribution` for every scale change on that transform. The first state a button shows, and any state applied before its scope is bound or while it is
inactive, is set at once; a later change tweens through the scope's `IMotionService`
([Reactive values and motion](../core/reactive-motion#motion)).

The strategy list uses `InterfaceReference<T>`, a serialized reference to a `UnityEngine.Object` that implements the
strategy interface. The drawer accepts a ScriptableObject asset or a `MonoBehaviour` component in the scene (dropping a
GameObject picks the matching component). A scene-authored strategy can therefore target a specific `Image` without a
hierarchy-name lookup.

The inspector is an IMGUI editor; a project that prefers another can plug one in through
`FoundationButtonInspectorPresentation.Factory`.

Short sound, haptic and punch responses belong to [Feedbacks](feedbacks), through `ButtonFeedbackBinder`.

## LocalizedText

`LocalizedText` subscribes to `ILocalizationService.Locale`, re-renders when the locale changes, supports changing its key
at runtime, and passes serialized or runtime arguments to the project's `ILocalizationFormatter`. The rendered text goes
through `ILocalizedTextTarget`, one method that your project implements for its text renderer, so `Foundation` stays
independent of any renderer. Details and a target example: [Localization](../app/localization#text-on-screen-localizedtext).

## Router, tabs and tooltips

- `UGuiViewFactory`, `TransformUIRoot` / `LayerUIRoot` and `UIRouterFactory` are the uGUI backend of the
  [UI router](router).
- `UITabGroup`, `UITabItem` and `UITabSelectionVisual` implement [tabs](router#tabs).
- `TooltipBubble` implements [tooltips](router#tooltips).
