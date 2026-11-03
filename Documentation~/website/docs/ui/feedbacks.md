---
title: Feedbacks
sidebar_position: 5
---

# Feedbacks

`Foundation.Feedbacks` plays short, composable responses to semantic events: a click sound plus a haptic tick, a reward
fanfare. A `FeedbackLibrary` holds `FeedbackDefinition` assets keyed by a stable `FeedbackId`; the service plays them
from gameplay, rewards, tutorials, sequences or UI.

| Type | Role |
| --- | --- |
| `FeedbackId` | A [typed id](../systems/typed-ids) for a definition. |
| `FeedbackDefinition` | A ScriptableObject: a repeat policy and a list of steps, each with a delay and a `FeedbackAction`. |
| `FeedbackLibrary` | A ScriptableObject holding the definitions. Keep it in project content. |
| `FeedbackAction` | A reusable ScriptableObject action. Built in: `PlayAudioFeedback` (an `AudioEventId`) and `HapticFeedbackAction` (a `HapticFeedbackKind`). |
| `IFeedbackService` | `Play(id, context)` returns a `FeedbackHandle`; `PlayAsync(id, context, ct)` awaits it. |
| `FeedbackContext` | The target `GameObject` and a position: `For(component)`, `At(position)`. |
| `ButtonFeedbackBinder` | Maps a `FoundationButton`'s `(state, signal)` to a feedback id. |
| `FeedbackPlayer` | A component with a `Play()` method for `UnityEvent`s and scripted sequences. |

## Wiring

Install the service with the library. It picks up `IAudioService`, `IHapticsService` and `IMotionService` if they are
registered; each is optional for the service. The package's audio and haptic actions do nothing without theirs. An action
that tweens calls `services.RequireMotion()`, which throws a clear error when no `IMotionService` is registered, instead
of animating on a hidden default.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Audio;
using Foundation.DI;
using Foundation.Feedbacks;
using UnityEngine;

public sealed class FeedbackWiring
{
    public static IFeatureInstaller Create(FeedbackLibrary library) => new FeedbackInstaller(library);
}

public sealed class RewardPresenter
{
    private readonly IFeedbackService _feedbacks;

    public RewardPresenter(IFeedbackService feedbacks) => _feedbacks = feedbacks;

    public void OnButtonPressed(Component button)
    {
        // Starts immediately; the handle can cancel it or tell whether it still plays.
        var handle = _feedbacks.Play(FeedbackId.From("ui.click"), FeedbackContext.For(button));
        if (!handle.IsPlaying)
            return;
    }

    public UniTask ShowLegendaryRewardAsync(GameObject reward, CancellationToken cancellationToken)
        => _feedbacks.PlayAsync(FeedbackId.From("reward.legendary"), FeedbackContext.For(reward), cancellationToken);
}

public static class FeedbackAuthoringExample
{
    // Normally authored as assets; this is what an authored "ui.click" definition contains.
    public static FeedbackDefinition CreateClick()
    {
        return FeedbackDefinition.Create(
            FeedbackId.From("ui.click"),
            new[]
            {
                new FeedbackStep(0f, PlayAudioFeedback.Create(AudioEventId.From("ui.click"))),
            });
    }
}
```

Audio actions refer to an `AudioEventId`; the project registers and preloads its [audio library](../systems/audio)
before playing feedback that uses it.

## Playback rules

- A definition starts its steps **in parallel**; each step has an optional delay (unscaled time).
- **Repeat policy.** `Restart` cancels the previous playback of the same id and target, waits for its cleanup, then
  starts again. `IgnoreWhilePlaying` returns the existing playback. Different targets play independently.
- **Cancellation and destruction.** Canceling or destroying the target stops pending steps. Custom motion actions should
  restore captured transform values in `finally`. `FeedbackPlayer` cancels on disable, so it is safe on pooled UI
  objects.
- Global audio and haptic actions do not need a target. A custom action should validate `FeedbackContext` as needed;
  the service does not skip it just because the context has no target or the target is inactive. Target destruction
  cancels playback by default. Set `RequiresTargetLifetime` when disabling the target should cancel playback; if the
  target is already disabled, cancellation can prevent the action from starting.
- Transform effects such as scale, position and rotation punches require custom or extension actions and a registered
  `IMotionService` implementation. The package does not include those transform actions, but provides a motion service
  (see [Motion](../core/reactive-motion#motion)).

## Custom actions

A custom action derives from `FeedbackAction`, receives the typed `FeedbackContext`, the optional `FeedbackServices` and
a cancellation token, and returns a `UniTask`:

```csharp compile
[CreateAssetMenu(menuName = "Game/Feedbacks/Log")]
public sealed class LogFeedbackAction : FeedbackAction
{
    [SerializeField] private string message = "feedback";

    public override UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken)
    {
        Debug.Log(message);
        return UniTask.CompletedTask;
    }
}
```

Particle effects are a project-specific action over your pooling abstraction.

## Buttons

`FoundationButton` emits semantic `Signals` (Click, PointerDown, PointerUp, Selected, Deselected, LongPress, Invalid).
It owns state, localization, interactability and async click handlers, but **does not choose sound, haptics or motion**.
Add a `ButtonFeedbackBinder` to the button and map `(state id, signal)` to a `FeedbackId`; an empty state id is a
fallback mapping. The binder can live on any `FoundationButton` without changing the button's gameplay API.

## What it is not

Feedback is a short response to an event. A UI transition owns a view's lifecycle animation, and a [scripted
sequence](../systems/sequences) coordinates longer ordered scenarios. Feedbacks implement neither, nor a timeline
editor, a particle renderer, or a second audio or haptics backend.
