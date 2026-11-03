---
title: Cloud save
sidebar_position: 4
---

# Cloud save

This integration builds cloud saves on the Google Play Games Saved Games service and connects it to the
[save system](../app/save) as one `ISaveRepositoryBackend`. Because `CompositeSaveRepository` picks the newest copy by timestamp on load and fans
every save out to all backends, an available cloud copy can be restored through the normal load flow after sign-in. A
cloud backend failure does not invalidate a successful local save.

| Type | Role |
| --- | --- |
| `GooglePlayGamesSignIn` | The sign-in of one app session: silent first (with a bootstrap-scene `WarmUp()`), then the interactive prompt, the player's toggle and a session history containing up to the eight most recent attempts. |
| `ICloudSavePreference`, `PlayerPrefsCloudSavePreference` | Where the player's on/off toggle lives. The game picks the key. |
| `GooglePlayGamesCloudSaveBackend` | The backend: snapshot open, read and commit; a read-before-write guard; the upload traffic policy. Returns no save when Play Games is unavailable or the snapshot is empty. |
| `CloudSaveUploadPolicy` | Upload gating: only when the content differs from the last upload and the minimum interval has elapsed. |
| `GooglePlayGamesSnapshotPayload` | The private binary envelope (`SaveData` ↔ `byte[]`). An implementation detail. |

## Requirements

The assembly is included on Android and in the Editor when both compilation requirements are met:

- `FOUNDATION_GAMEFRAMEWORK` is defined (see [GameFramework overview](overview#enabling-the-module));
- the `com.google.play.games` package is installed (it sets `FOUNDATION_GOOGLE_PLAY_GAMES`).

Saved Games must also be enabled for the app in Play Console for cloud operations to work. This is a runtime service
configuration requirement, not a compilation condition. In the Editor, the adapter remains available for composition,
but sign-in and cloud reads/writes are disabled.

## Wiring

Sign-in starts before any container exists, so the game creates one `GooglePlayGamesSignIn` in the bootstrap scene,
calls `WarmUp()`, and registers that same instance in the container. The backend is then one more
`ISaveRepositoryBackend` next to the local one.

```csharp compile
using System;
using Foundation.DI;
using Foundation.GameFramework.CloudSave;
using Foundation.Save;

public sealed class CloudSaveInstaller : IFeatureInstaller
{
    private readonly GooglePlayGamesSignIn _signIn;

    public CloudSaveInstaller(GooglePlayGamesSignIn signIn) => _signIn = signIn;

    public static GooglePlayGamesSignIn CreateSignIn()
        => new GooglePlayGamesSignIn(
            new PlayerPrefsCloudSavePreference("MyGame.CloudSave"),
            GooglePlayGamesSignIn.DefaultAuthenticationTimeout);

    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance(_signIn);
        registry.Register(_ => new CloudSaveUploadPolicy(TimeSpan.FromSeconds(60)), ServiceLifetime.Singleton);
        registry.Register(
            resolver => new GooglePlayGamesCloudSaveBackend(
                "my-game",
                resolver.Resolve<GooglePlayGamesSignIn>(),
                resolver.Resolve<CloudSaveUploadPolicy>()),
            ServiceLifetime.Singleton).As<ISaveRepositoryBackend>();
    }
}
```

The `CompositeSaveRepository` in your [save installer](../app/save) then receives both the local and the cloud backend
through `IEnumerable<ISaveRepositoryBackend>`.

The upload policy uses the save's `SaveData.SavedAtUtcUnixMilliseconds` timestamp to enforce the minimum interval and
write the snapshot description. This is the save timestamp, not the time the cloud upload actually occurs.

## Letting the player turn it off

`GooglePlayGamesSignIn.SetEnabled(bool)` writes the toggle through the preference, `IsPreferenceEnabled` reads it, and
`TryEnableAsync` runs the interactive prompt when the player turns the feature on. `AuthenticationReported` and
`AuthenticationHistory` expose the retained `GooglePlayGamesAuthReport` records (stage, status, elapsed time and device
diagnostics). Each report is logged through `IAppLogger`; any forwarding depends on the configured logger. The log and
`ToParameters()` can include device details, a signing-certificate SHA-1 and exception details. Configure logging and
send only the fields needed under the project's consent policy.

The read-before-write guard and upload history are independent for each account and snapshot/slot. A failed reread
leaves that snapshot unknown; reading another slot cannot authorize its upload. Content comparison includes the save
version. The first upload of a slot is allowed immediately; later changed uploads obey its own minimum interval.

The backend reports skipped writes through `ISaveRepositoryBackendWithResult`. Calling its `SaveAsync` directly throws
when no write was confirmed; compose it with the local backend to tolerate skips. SDK write failures are reported as
errors. Canceling an operation stops waiting for SDK callbacks. An already submitted commit may still finish later;
cancellation does not roll it back.

The adapter assembly also compiles in the Editor when the active build target is a desktop platform.
