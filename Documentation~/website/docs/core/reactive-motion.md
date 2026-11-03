---
title: Reactive values and motion
sidebar_position: 8
---

# Reactive values and motion

Two contracts for things that change over time. Neither pulls in a library: a tween library (LitMotion) is an optional adapter.

## Reactive values

`Foundation.Reactive` is a deliberately small surface:

| Type | Role |
| --- | --- |
| `IEventStream<T>` | `Subscribe(Action<T>)` returns an `IDisposable`. |
| `IReadOnlyReactiveValue<T>` | An event stream with a current `Value`. |
| `EventStream<T>` | A stream you `Publish` to. A throwing subscriber is logged and does not stop the others. |
| `ReactiveValue<T>` | A settable value. Setting an equal value publishes nothing. **Subscribing delivers the current value immediately**, then every change. |

The initial callback runs inside `Subscribe`: if it throws, the subscription is removed and the exception propagates.
Later notifications use `EventStream<T>` and log subscriber exceptions. After `ReactiveValue<T>.Dispose()`, a new
subscription still receives the current value once, but does not receive later changes.

Expose the read-only interface and keep the mutable one private:

```csharp compile
using System;
using Foundation.Reactive;

public sealed class Wallet : IDisposable
{
    private readonly ReactiveValue<int> _gold = new(0);
    private readonly EventStream<int> _spent = new();

    public IReadOnlyReactiveValue<int> Gold => _gold;

    public IEventStream<int> Spent => _spent;

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || amount > _gold.Value)
            return false;

        _gold.Value -= amount;
        _spent.Publish(amount);
        return true;
    }

    public void Add(int amount) => _gold.Value += amount;

    public void Dispose()
    {
        _gold.Dispose();
        _spent.Dispose();
    }
}

public sealed class GoldLabel : IDisposable
{
    private readonly IDisposable _subscription;

    public GoldLabel(Wallet wallet)
    {
        // Runs once now with the current value, then on every change.
        _subscription = wallet.Gold.Subscribe(OnGoldChanged);
    }

    public void Dispose() => _subscription.Dispose();

    private void OnGoldChanged(int gold)
    {
        // Update the text.
    }
}
```

`ILocalizationService.Locale` and the `FoundationButton` click and state streams use these same contracts.

### Owning subscriptions

`DisposableGroup` owns a set of disposables and releases them together: the subscriptions of one view or screen.
`AddTo` hands a subscription to a group where it is made.

```csharp compile
using System;
using Foundation.Reactive;

public sealed class WalletView : IDisposable
{
    private readonly DisposableGroup _subscriptions = new();

    public WalletView(Wallet wallet)
    {
        wallet.Gold.Subscribe(OnGoldChanged).AddTo(_subscriptions);
        wallet.Spent.Subscribe(OnSpent).AddTo(_subscriptions);
    }

    public void Dispose() => _subscriptions.Dispose();

    private void OnGoldChanged(int gold) { }

    private void OnSpent(int amount) { }
}
```

- `Clear()` releases everything and the group stays usable (a list that is rebuilt); `Dispose()` ends it, and anything
  added afterwards is released at once, so a late callback cannot leak a subscription.
- Items are released newest first. One that throws does not keep the others from being released; the failure is reported
  once all of them are.
- `Add(Action)` runs a callback on release, `Remove(item)` releases one item early. The group is for the main thread.

## Motion

`IMotionService` is the contract components use to tween a value. Foundation includes `PlayerLoopMotionService`, a small
UniTask loop per motion on the player loop's `Update` using game time. It keeps no per-motion state, so one instance can
serve the scope. The optional LitMotion adapter is `Foundation.Infrastructure.LitMotion` (`LitMotionService`,
`LitMotionInstaller`); it is available when `com.annulusgames.lit-motion` is installed.

Neither service is registered automatically. Register one `IMotionService` in the root scope. For LitMotion, call
`new LitMotionInstaller().Install(registry)` or add the installer to the project's feature installers. Components receive
the service from their scope when the game-object injector binds them.

The router and tabs do not tween. Button scale and color strategies tween only when `duration` is above zero and the
button is already showing a state; the first state and states shown while the button is inactive are applied immediately.
A button strategy or feedback action that needs motion calls `RequireMotion()` on its context
(`FoundationButtonStrategyContext` or `FeedbackServices`), which throws if no service is registered.
`MotionEasing.Evaluate(ease, t)` evaluates curves for engines that need them.

`Animate(from, to, duration, ease, state, onUpdate, loop, lifetime)` returns an `IMotionHandle` (`IsActive`, `Cancel()`,
`WaitAsync()`). It animates `float`, `Vector2`, `Vector3`, `Quaternion` and `Color`. `MotionEase` lists the usual
easing curves; `MotionLoop` is `Once`, `PunchOut` (two steps, yoyo) or a custom count with optional yoyo.

The tween takes the object it updates (`state`) and a callback that receives the value and that object. A `static`
callback avoids a closure in the calling code. When a lifetime token is supplied, the LitMotion adapter also creates a
release delegate; allocations inside LitMotion itself are not measured here. A plain `Action<T>` overload is available,
but a capturing lambda may allocate. A custom `IMotionService` only needs to implement the `float` method; the other value
types fall back to it.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Motion;
using UnityEngine;

public sealed class PanelSlide
{
    private readonly IMotionService _motion;

    public PanelSlide(IMotionService motion) => _motion = motion;

    public UniTask SlideInAsync(RectTransform panel, CancellationToken cancellationToken)
    {
        var handle = _motion.Animate(
            new Vector2(-600f, 0f),
            Vector2.zero,
            0.25f,
            MotionEase.OutCubic,
            panel,
            static (position, rect) => rect.anchoredPosition = position,
            lifetime: cancellationToken);

        return handle.WaitAsync(cancellationToken);
    }
}
```

Code that can work without motion may resolve `IMotionService` with `TryResolve` (see
[optional dependencies](di#optional-dependencies)) and set the end value itself when no service is registered. If code
should use the motion API without a visible tween, register a custom `IMotionService` that applies the end value
immediately. The built-in button strategies and feedback actions that call `RequireMotion()` still need a registered
service.
