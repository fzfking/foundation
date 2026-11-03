---
title: Addressables
sidebar_position: 4
---

# Addressables

`Foundation.Infrastructure.Addressables` implements [`IAssetProvider`](../app/content) on top of Unity Addressables and
adds an offline-capable content startup step to the [boot pipeline](../concepts/app-composition). It compiles in when
`com.unity.addressables` is present. The verified version is **4.0.1** on Unity 6000.5. An older 2.x line failed to
compile there because of an obsolete `GetInstanceID()` overload. Re-check compatibility before pinning a different
version.

Addressables types stay in this assembly. Features and the rest of the package see only `IAssetProvider`, so the
delivery backend can change without touching them.

| Type | Role |
| --- | --- |
| `AddressablesProvider` | The `IAssetProvider`. Concurrent requests for one address share a load, and handles are released when the last reference goes. |
| `AddressablesContentInitializer` | Initializes Addressables from local or cached content when possible and exposes status for a connection-waiting screen. |
| `ContentStartupBootStep` | The content phase of the boot pipeline: initialize, run an optional gate, then download what is missing. |
| `AddressablesReleaseGate` | Applies the [release configuration](../app/release) to a starting client and says whether it must update first. |
| `AddressablesEndpointRouting` | Routes Addressables to a release's content folder. |
| `IContentHostReachabilityChecker`, `UnityWebRequestHostReachabilityChecker` | The initializer uses this checker to probe a URL that you provide. |
| `AnyContentHostReachabilityChecker` | Reports reachable when any configured content-host probe URL responds, and reports the device offline only when every one does; useful when synchronizing through mirrors. |

To route synchronization attempts through multiple origins, pass a `ContentMirrorCoordinator`; see
[Content mirroring](../app/content-mirroring). The coordinator probes every mirror at the start of each pass and keeps the
last successful mirror first for later passes in the same process; that choice is not persisted. In mirror mode the
synchronizer uses the reachability checker only for the device network state
(`INetworkReachabilityStatusProvider.IsNetworkUnavailable`). `NotReachable` is the only hard offline signal: a failed
host probe alone is unknown and does not stop the pass. When the whole pass fails, the synchronizer restores the
configured Addressables endpoint and keeps retrying through `WaitingForConnection`; unknown errors add no public status,
retry limit, or terminal exception. Addressables group and packing settings remain project-owned.

## The provider

Register `AddressablesProvider` as the `IAssetProvider`, optionally behind a queue that spreads loads over frames.

```csharp compile
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Infrastructure.Addressables;
using Foundation.Spawning;

public sealed class AddressablesContentInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<IObjectSpawner>(_ => new AsyncObjectSpawner(AsyncObjectSpawner.DefaultIntegrationTimeMS), ServiceLifetime.Singleton);
        registry.Register(
            resolver => new QueuedAssetProvider(
                new AddressablesProvider(
                    resolver.Resolve<Foundation.Logging.IAppLogger>(),
                    resolver.Resolve<IObjectSpawner>())),
            ServiceLifetime.Singleton).As<IAssetProvider>();
    }
}
```

`InstantiateAsync` loads the prefab and creates the object through the `IObjectSpawner` (the shared per-frame
instantiation budget); the instance keeps the prefab alive and releases it on dispose.

## Startup: offline first

`ContentStartupBootStep` is the content phase of boot. It initializes against the local or cached catalog, runs an
optional gate, then downloads what is missing, showing "waiting for connection" on the loading curtain meanwhile. The
content synchronization retries failed downloads until it succeeds or boot is canceled. An Addressables initialization
failure is reported to the caller: the underlying failed operation cannot be assumed to restart on another call.

`AddressablesContentInitializer` is the part you can use alone:

- With a reachability checker, it avoids an online initialization attempt when there is no connection and no local cache, and
  still attempts initialization offline when a cache exists (Addressables' catalog-hash fallback then decides).
- `State` and `StateChanged` let a caller show a connection-waiting UI before initialization.
- With no checker, it calls Addressables directly.
- Concurrent or repeated calls share one initialization result. The first call controls the operation's cancellation and
  progress; later callers can cancel their own wait. A failed or canceled result remains associated with that initializer.

The release check plugs in as the step's gate. Start loading the configuration at once and evaluate it as the gate, so
it adds no extra wait; a client that must update stops the boot before it downloads content it cannot play.

```csharp compile
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;
using Foundation.Release;
using UnityEngine;

public sealed class ContentBootstrap : BootstrapEntryPoint
{
    private ILoadingCurtain _curtain;

    protected override ILoadingCurtain Curtain => _curtain;

    protected override IReadOnlyList<IBootStep> CreateSteps()
    {
        var gate = CreateReleaseGate();

        return new IBootStep[]
        {
            new ContentStartupBootStep(
                new UnityWebRequestHostReachabilityChecker("https://cdn.example.com/ping"),
                _curtain,
                () => new BootMessage("No connection", "Waiting for the network..."),
                afterInitialize: cancellationToken => RequireUpdateAsync(gate)),
            new SceneBootStep(RootSceneIndex),
        };
    }

    private static AddressablesReleaseGate CreateReleaseGate()
    {
        var provider = new ReleaseConfigProvider(
            "https://example.com/release.json",
            new UnityWebRequestReleaseConfigSource(timeoutSeconds: 10),
            new FileReleaseConfigCache(Path.Combine(Application.persistentDataPath, "release.json")),
            BuiltInConfig());

        return new AddressablesReleaseGate(
            provider.LoadAsync(CancellationToken.None),
            environmentId: "production",
            contentRoot: "https://cdn.example.com/content",
            fallbackStoreUrl: "https://example.com/store",
            clientVersionText: Application.version);
    }

    private static async UniTask<BootStepOutcome> RequireUpdateAsync(AddressablesReleaseGate gate)
    {
        var result = await gate.EvaluateAsync();
        if (!result.UpdateRequired)
            return BootStepOutcome.Continue;

        Debug.LogWarning($"Update required: {result.StoreUrl}");
        return BootStepOutcome.Stop;
    }

    private static ReleaseConfig BuiltInConfig() => new()
    {
        schema = ReleaseConfig.SupportedSchema,
        client = new ClientPolicy { minimum = "1.0.0", recommended = "1.0.0", storeUrl = string.Empty },
        environments = new[]
        {
            new ReleaseEnvironment
            {
                id = "production",
                current = new ReleaseSlot { release = "built-in", minClient = "1.0.0", path = "Prod" },
            },
        },
    };
}
```

## Choosing the startup strategy

Offline-first is the default of `ContentStartupBootStep`, and a project can switch it off. The choice is a
`ContentStartupStrategy` (in `Foundation.Content`, the same type the [YooAsset](yooasset#choosing-the-startup-strategy)
step uses), normally read from a project setting:

| Strategy | What the step does |
| --- | --- |
| `OfflineFirst` (default) | Everything described above: initialize from the local or cached catalog, run the gate, synchronize the whole catalog, wait for a connection while the device has nothing usable. |
| `Standard` | Addressables' own start and nothing more: `InitializeAsync` once, then the gate. Nothing is downloaded ahead of time, nothing waits for the network, and a failed initialization throws. Content that is not on the device is fetched when it is first loaded. |

```csharp compile
using System.Collections.Generic;
using Foundation.Content;
using Foundation.Infrastructure.Addressables;
using Foundation.Lifecycle;

public sealed class ContentDeliverySettings
{
    public bool OfflineFirst { get; set; } = true;
}

public static class ContentBootSteps
{
    public static IBootStep Create(
        ContentDeliverySettings settings,
        IContentHostReachabilityChecker reachability,
        ILoadingCurtain curtain)
        => new ContentStartupBootStep(
            reachability,
            curtain,
            () => new BootMessage("No connection", "Waiting for the network..."),
            strategy: settings.OfflineFirst ? ContentStartupStrategy.OfflineFirst : ContentStartupStrategy.Standard);
}
```

The setting belongs to the project (a config row, a build profile), not to Foundation. `Standard` fits a game that ships
its content inside the player, or one that fetches content on demand with `EnsureLabelAsync` or plain loads. It also
changes which Addressables settings matter: the table below is the offline-first configuration. With `Standard` leave
*Disable Catalog Update On Start* off, so that Addressables itself looks for a newer catalog at start, since the
synchronizer that turns updates back on does not run.

## Configuring offline-first content

Offline-first startup also depends on the Addressables configuration. The following settings are a starting point for a
mobile game that delivers content through a CDN; adapt them to the project's delivery and caching requirements.

**Settings asset (`AddressableAssetSettings`)**

| Setting | Value | Why |
| --- | --- | --- |
| Build Remote Catalog | on | The game can pick up new content without a store update. |
| Bundle Local Catalog | on | The catalog shipped in the player is the offline starting point; without it a fresh install cannot even read the address table. |
| Disable Catalog Update On Start | on | Initialization starts from local content. The synchronizer controls catalog updates; in mirror mode it first checks the catalog files on the release endpoint. |
| Optimize Catalog Size | on | A smaller catalog is cheaper to download on every update. |
| Catalog Requests Timeout | 15 s | A dead host should fail the check quickly and fall back to the cached catalog. |
| Build Addressables On Player Build | on | The catalog in the player always matches the player's code. |
| Remote Catalog Build / Load Path | `Remote.BuildPath` / `Remote.LoadPath` | The catalog sits next to the bundles it describes. |
| Non-Recursive Building, Contiguous Bundles | on | Stable, smaller bundles: an update re-downloads only what really changed. |

**Every bundled group**

| Setting | Value | Why |
| --- | --- | --- |
| Build / Load Path | `Remote.BuildPath` / `Remote.LoadPath` | The content is downloaded, not shipped in the player. Put a group on `Local.*` to ship it inside the build, so a fresh install can start from it with no network. |
| Use Asset Bundle Cache | on | Downloaded bundles stay on the device; that cache is what an offline start reads. |
| Use Asset Bundle CRC (also for cached bundles) | on | A corrupted cache fails loudly and is downloaded again rather than crashing in a loader. |
| Request Timeout / Retry Count | 30 s / 3 | Slow mobile networks survive a hiccup without the boot reporting an error. |
| Bundle Naming | No hash | File names stay stable; the catalog carries each bundle's hash and the device cache is keyed by it, so a changed bundle is detected without a new name. Make sure the CDN does not cache the catalog and its hash file for long. |

**Profiles.** Keep one profile per environment, differing only in the paths:

| Variable | Production | Development |
| --- | --- | --- |
| `Remote.BuildPath` | `ServerData/Prod/[BuildTarget]` | `ServerData/Dev/[BuildTarget]` |
| `Remote.LoadPath` | `https://cdn.example.com/Prod/[BuildTarget]` | `https://cdn.example.com/Dev/[BuildTarget]` |
| `ContentState.BuildPath` | `Library/AddressablesContentState/Prod/[BuildTarget]` | `Library/AddressablesContentState/Dev/[BuildTarget]` |

Keeping the content-state file per environment matters: it is what an incremental content update compares against, so
one environment's build must never be diffed against another's. Select the profile as part of the project's build
process to prevent a release build from using the development URL. A build uploads the contents of
`ServerData/<Env>/<Target>` to the matching CDN folder; upload it **before** the player reaches users.

**Grouping.** Startup synchronization collects locations with dependencies from the available catalogs and downloads
their dependencies. Do not split content into "optional" groups and expect them to be skipped. If a part
must be downloaded later, it belongs in a different catalog or in a [game module](../app/game-modules).

**What to expect**

- The first launch of a build whose groups are all remote needs a connection to download the bundles. If the local
  catalog initializes successfully, synchronization shows the "waiting for connection" state until the network returns.
- After a successful sync, startup can use the cached content offline while that cache remains intact. In mirror mode,
  the release endpoint is checked once for catalog files; if the precheck fails, startup uses the current catalog.
- After a catalog update, startup downloads dependencies of the collected locations and waits if the download fails.
  This does not verify that every mirror contains the same version of the content.

To verify the setup, install the build, let it sync once, switch the device to airplane mode and start it again: it must
reach the first screen without the waiting state. Then clear the app data and start it offline. If initialization of the
local catalog succeeds, the content synchronization should show the waiting state; an initialization failure is reported
as an error.

## Lifetime

Asset lifetime is explicit. Every load returns a handle that owns its reference, and `AssetHandleScope` releases a group
of them together; see [Content](../app/content). Do not call `Addressables.Release` from gameplay code. Lifetime policy
belongs to the asset provider and the UI infrastructure, and repeated requests for the same persistent asset share one
load instead of creating duplicates.

## Grouping

Addressables groups and labels describe delivery. They should optimize maintainability and download size, not mirror
your code namespaces: a few logical groups (core, UI, audio, localization, configs, content packs) are usually enough,
not one per feature or per asset. They do not define gameplay schemas; see
[Architecture](../concepts/architecture#content-authoring-is-separate-from-delivery).
