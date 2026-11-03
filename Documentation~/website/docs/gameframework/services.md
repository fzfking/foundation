---
title: Platform services
sidebar_position: 5
---

# Platform services

Small GameFramework contracts for things the platform owns. Most ship the contract plus a default or a null
implementation; a project supplies the SDK-backed one where the table says so.

| Service | Contract | Ships |
| --- | --- | --- |
| [Connectivity](#connectivity) | `IConnectivityService` | `UnityConnectivityService` |
| [In-app purchases](#in-app-purchases) | `IIapService` | contract only |
| [Identity](#identity) | `IUserIdentityProvider` | `LocalUserIdentityProvider` |
| [Notifications](#notifications) | `IPushNotificationService` | spacing queue; `MobileNotificationService` with `com.unity.mobile.notifications` |
| [Sharing](#sharing) | `IClipboardService`, `IUrlLauncher`, `ISharingService` | clipboard and URL launcher; `NullSharingService` |
| [System permissions](#system-permissions) | `IPermissionService` | contract only |
| [Device diagnostics](#device-diagnostics) | `IDeviceDiagnostics` | `UnityDeviceDiagnostics` |

All of these need `FOUNDATION_GAMEFRAMEWORK` ([enabling](overview#enabling-the-module)).

## Connectivity

`IConnectivityService` combines a cheap, interval-polled local check (`Application.internetReachability`, driven by the
[scheduler](../core/time-scheduling-random), not every frame) with an optional, less frequent real HTTP health check to
a URL you supply, expecting a 200. A reachable network interface does not prove internet access (for example, Wi-Fi without internet or a captive portal).
When a URL is configured, the HTTP check determines connectivity only if the local check sees a network interface; no
interface yields `Offline`. While the first HTTP check is pending, the state is `Unknown`.

`State` is `Unknown`, `Offline` or `Online`; `StateChanges` is an `IReadOnlyReactiveValue<ConnectivityState>` that
delivers the current value on subscribe (see [Reactive](../core/reactive-motion)).

`Dispose` stops the reachability poll and the health check schedule (when a URL is configured), cancels an in-flight
health check and discards its result. Once `Dispose` returns, the state no longer changes and `StateChanges` publishes
nothing. Calling `Subscribe` on `StateChanges` after that throws `ObjectDisposedException`, including through a
reference saved earlier, and the callback is not invoked. `Value` and `State` stay readable and keep the last value.

```csharp compile
using System;
using Foundation.GameFramework.Connectivity;
using Foundation.Scheduling;

public sealed class OfflineBanner : IDisposable
{
    private readonly UnityConnectivityService _connectivity;
    private readonly IDisposable _subscription;

    public OfflineBanner(IScheduler scheduler, Action<bool> setVisible)
    {
        _connectivity = new UnityConnectivityService(scheduler, healthCheckUrl: "https://api.example.com/health");
        _subscription = _connectivity.StateChanges.Subscribe(state => setVisible(state == ConnectivityState.Offline));
    }

    public void Dispose()
    {
        _subscription.Dispose();
        _connectivity.Dispose();
    }
}
```

## In-app purchases

`IIapService` models a product catalog, purchase, restore and a separate confirm (acknowledge) step, shaped to fit both
Google Play Billing-style APIs and server-mediated gateways. Receipt data stays an opaque string in
`PurchaseRecord.ReceiptData`, since its format is store-specific and Foundation never parses it. A project implements
the interface over its store SDK.

This abbreviated example marks where the project should verify the receipt with its store or server and grant the
entitlement idempotently. Complete that step before confirming the purchase; the validation and grant implementation is
project-specific.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.GameFramework.Iap;

public sealed class Shop
{
    private readonly IIapService _iap;

    public Shop(IIapService iap) => _iap = iap;

    public async UniTask<bool> BuyAsync(string productId, CancellationToken cancellationToken)
    {
        var result = await _iap.PurchaseAsync(productId, cancellationToken);
        if (!result.Success)
            return false;

        // Verify the receipt with the store/server and grant the entitlement idempotently here.
        return await _iap.ConfirmPurchaseAsync(result.Purchase, cancellationToken);
    }

    public UniTask<IReadOnlyList<PurchaseRecord>> RestoreAsync(CancellationToken cancellationToken)
        => _iap.RestorePurchasesAsync(cancellationToken);
}
```

## Identity

`IUserIdentityProvider` exposes a `UserId` and a `SessionId` that a project can use to correlate events.
`LocalUserIdentityProvider` generates an anonymous user id with `IGuidGenerator` and persists it in `PlayerPrefs`.
It generates a new session id whenever the provider is constructed, so register one instance for the desired session
lifetime. Analytics and error reporting do not consume this interface automatically. A project can supply a
server-authenticated provider behind the same interface.

## Notifications

`IPushNotificationService` has `RequestAuthorizationAsync`, `ScheduleLocal(LocalNotification)` and `CancelAll`.
`MobileNotificationService` schedules through `NotificationSpacingQueue`, which pushes a requested fire time forward until it is at least `minimumSpacing` (30 minutes by default) from the
already reserved times. It does not search for an earlier free slot. This helps prevent a batch of calls from firing
everything at once. The service (in the optional `Notifications/Mobile` assembly) implements the contract over
`com.unity.mobile.notifications`; other implementations decide their own scheduling behavior.

```csharp compile
using System;
using Foundation.GameFramework.Notifications;

public sealed class ComeBackReminder
{
    private readonly IPushNotificationService _notifications;

    public ComeBackReminder(IPushNotificationService notifications) => _notifications = notifications;

    public void Schedule()
    {
        _notifications.CancelAll();
        _notifications.ScheduleLocal(new LocalNotification
        {
            Id = "come_back",
            Title = "Your harvest is ready",
            Body = "Come back and collect it.",
            FireAtUtc = DateTime.UtcNow.AddHours(8),
        });
    }
}
```

## Sharing

- `IClipboardService`/`ClipboardService`: copy and read text, by default through `GUIUtility.systemCopyBuffer`.
- `IUrlLauncher`/`UnityUrlLauncher`: open an allowed URL through `Application.OpenURL`. Cancellation is checked before
  launch; once the system handler starts, the caller cannot cancel it.
- `ISharingService`/`NullSharingService`: the native share-sheet contract (`ShareAsync(SharePayload)`). Only a no-op
  ships, because a real share sheet needs a platform plugin you choose.

## System permissions

`IPermissionService` checks or requests an OS permission by string id (`CheckAsync`, `RequestAsync`, returning
`Granted`, `Denied` or `NotDetermined`). It answers "has the OS granted this app permission Y", which is a different
question from [requirements](../core/requirements-rewards) ("can the player do X right now"). A project supplies the
platform-specific implementation.

## Device diagnostics

`IDeviceDiagnostics` lets a project warn the player before a risky operation, such as downloading a large content
update, instead of risking a crash. `UnityDeviceDiagnostics` reports `TotalSystemMemoryMb`, `TryGetAvailableStorageMb`
and `HasEnoughStorageFor`. Account for platform limits: this backend reports total system memory, not the memory
available to the application. `HasEnoughStorageFor` returns `true` when available space cannot be measured, so `true`
means either that enough space was found or that the check was unavailable. Use `TryGetAvailableStorageMb` when the
project needs to distinguish those cases.

Device resource checks and [operation tracing](../core/diagnostics#operation-tracing) are separate mechanisms.
