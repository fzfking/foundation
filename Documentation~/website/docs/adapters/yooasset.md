---
title: YooAsset
sidebar_position: 5
---

# YooAsset

`Foundation.Infrastructure.YooAsset` is an alternative [`IAssetProvider`](../app/content) backed by
[YooAsset](https://www.yooasset.com/). It compiles in when `com.tuyoogame.yooasset` is present, and compiles out cleanly
otherwise, so a project that only uses [Addressables](addressables) never sees this assembly. Pick one of the two as
your delivery backend; nothing above `IAssetProvider` changes.

The provider is verified against YooAsset 3.0.5 on Unity 6000.5 in editor-simulate mode (shared loads, release counts as
YooAsset sees them, cancellation, failure recovery, instantiate and destroy, preload, provider disposal). The host mode,
offline-first synchronizer, and standard startup all compile against YooAsset 3.0.5, but have not been tested against a
live CDN; try the airplane-mode check described for [Addressables](addressables#configuring-offline-first-content) on a
device before relying on it.

| Type | Role |
| --- | --- |
| `YooAssetProvider` | The `IAssetProvider`. Loads share one YooAsset handle per address with reference-counting behavior similar to `AddressablesProvider`: concurrent requests join one load, a caller that cancels gives its reference back without canceling the load for the others, and the handle is released when the last reference goes. |
| `YooAssetContentInitializer` | Package setup, called before the provider is used. |
| `IYooAssetContentSettings` | `PackageName`, `UseEditorSimulation`, `UseOfflinePlayerMode`. |
| `IYooAssetRemoteContentSettings` | Optional: adds `RemoteUrl`, `BuiltinPackageVersion` and the `StartupStrategy`, which switches the initializer to host play mode. |
| `YooAssetContentSynchronizer` | The offline-first sync, used when the settings choose that strategy: starts on the version the device holds, updates from the host when it can. |
| `IYooAssetVersionStore`, `PlayerPrefsYooAssetVersionStore` | Remembers the last version that was downloaded in full. |
| `YooAssetContentStartupBootStep` | The content phase of the [boot pipeline](../concepts/app-composition) for a host-mode package. |
| `YooAssetPackageContext` | The package handle the provider reads; the initializer publishes it. |
| `ContentMirrorCoordinator` | Optional mirror selection and failover for offline-first synchronization. |

For retrying synchronization through multiple remote origins, pass a `ContentMirrorCoordinator` to the synchronizer
and use the supplied mirror callback; see [Content mirroring](../app/content-mirroring).

## Setup

The initializer does editor-simulate mode in the Editor and offline player mode in a build. For content delivered from a
host, see [Offline-first content from a host](#offline-first-content-from-a-host).

```csharp compile
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Infrastructure.YooAsset;
using Foundation.Spawning;

public sealed class ContentSettings : IYooAssetContentSettings
{
    public string PackageName => "DefaultPackage";

    public bool UseEditorSimulation => UnityEngine.Application.isEditor;

    public bool UseOfflinePlayerMode => !UnityEngine.Application.isEditor;
}

public sealed class YooAssetContentInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<ContentSettings>(ServiceLifetime.Singleton).As<IYooAssetContentSettings>();
        registry.Register<YooAssetPackageContext>(ServiceLifetime.Singleton);
        // This setup creates the initializer without an optional synchronizer.
        registry.Register(
            resolver => new YooAssetContentInitializer(
                resolver.Resolve<IYooAssetContentSettings>(),
                resolver.Resolve<YooAssetPackageContext>()),
            ServiceLifetime.Singleton);
        registry.Register<IObjectSpawner>(_ => new AsyncObjectSpawner(AsyncObjectSpawner.DefaultIntegrationTimeMS), ServiceLifetime.Singleton);

        registry.Register(
            resolver => new QueuedAssetProvider(
                new YooAssetProvider(
                    resolver.Resolve<YooAssetPackageContext>(),
                    resolver.Resolve<Foundation.Logging.IAppLogger>(),
                    resolver.Resolve<IObjectSpawner>())),
            ServiceLifetime.Singleton).As<IAssetProvider>();
    }
}

public sealed class YooAssetStartup
{
    private readonly YooAssetContentInitializer _initializer;

    public YooAssetStartup(YooAssetContentInitializer initializer) => _initializer = initializer;

    public UniTask InitializeAsync(System.Threading.CancellationToken cancellationToken)
        => _initializer.InitializeAsync(cancellationToken);
}
```

Await `InitializeAsync` before anything loads an asset; the provider throws if the package is not yet published.

## Offline-first content from a host

Implement `IYooAssetRemoteContentSettings` instead of the plain settings and the initializer uses YooAsset's host play
mode (the build's own files plus a sandbox cache that downloads from `RemoteUrl`). Editor simulation and offline player
mode still win when their flags are set, so the same settings class works in the Editor.

### Choosing the startup strategy

What happens at start is the project's choice, `IYooAssetRemoteContentSettings.StartupStrategy`, a
`ContentStartupStrategy` (in `Foundation.Content`, shared with the
[Addressables](addressables#choosing-the-startup-strategy) step). Read it from a project setting to make offline-first
conditional:

| Strategy | What the initializer does |
| --- | --- |
| `OfflineFirst` | The synchronizer described below. It is required: the initializer throws on construction without one. |
| `Standard` | YooAsset's plain host start: the gate runs, then the newest package version is requested from the host and its manifest loaded. Files are fetched when they are first loaded, and an unreachable host fails the initialization. No synchronizer is needed. |

Host play mode normally requests the package version from the host at startup. `YooAssetContentSynchronizer` can start
with a complete local version and update it when the host is reachable:

1. The `beforeRemoteUpdate` gate runs before loading a manifest or contacting the host. A client that must update can
   stop here before downloading content.
2. The synchronizer checks the last fully downloaded version in `IYooAssetVersionStore`. If it cannot use that version,
   it tries `BuiltinPackageVersion`. YooAsset checks whether any bundles still need downloading; a version with missing
   bundles is not used for offline startup. This uses YooAsset’s catalog and cache state; it is not a separate integrity
   check of every file. If the manifest is missing from the cache, YooAsset may request it from the host.
3. When a complete local version is available, it is activated. If the host is reachable, the synchronizer prefetches
   the newest manifest and downloads its missing bundles before activating it. A failed download leaves the local version
   active. Cancellation stops the downloader; an existing active manifest is restored if activation has already changed it.
4. Without a complete local version, the synchronizer waits for a connection and retries until it downloads a complete
   version. `StateChanged` reports `WaitingForConnection` for the loading screen.

For the first offline launch, include the complete YooAsset package in the build and set `BuiltinPackageVersion` to its
version. In host mode the initializer asks YooAsset to copy the built-in manifest into its cache. The configured version
must match the version included in the build. If another owner initializes the package, that owner must configure the
built-in and cache file systems accordingly. Verify the setup on the target device before relying on offline startup.

```csharp compile
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.Infrastructure.YooAsset;
using Foundation.Lifecycle;
using UnityEngine;

public sealed class HostedContentSettings : IYooAssetRemoteContentSettings
{
    public ContentStartupStrategy StartupStrategy => ContentStartupStrategy.OfflineFirst;

    public string PackageName => "DefaultPackage";

    public bool UseEditorSimulation => Application.isEditor;

    public bool UseOfflinePlayerMode => false;

    public string RemoteUrl => "https://cdn.example.com/content/DefaultPackage";

    // Replace this placeholder with the version of the built-in YooAsset package.
    public string BuiltinPackageVersion => "1.0.0";
}

public static class HostedContentBoot
{
    public static IBootStep CreateStep(
        HostedContentSettings settings,
        YooAssetPackageContext context,
        ILoadingCurtain curtain)
    {
        var synchronizer = new YooAssetContentSynchronizer(
            new PlayerPrefsYooAssetVersionStore(),
            cancellationToken => IsHostReachableAsync(cancellationToken));
        var initializer = new YooAssetContentInitializer(settings, context, synchronizer);

        return new YooAssetContentStartupBootStep(
            initializer,
            synchronizer,
            curtain,
            () => new BootMessage("No connection", "Waiting for the network..."));
    }

    private static UniTask<bool> IsHostReachableAsync(CancellationToken cancellationToken)
        => UniTask.FromResult(Application.internetReachability != NetworkReachability.NotReachable);
}
```

With the `Standard` strategy, build the initializer without a synchronizer and pass `null` for it to the step (or keep
the code above and let the settings decide: a synchronizer that is not used costs nothing).

The initializer outlives the boot step, so register it (and the synchronizer) in the root scope rather than creating them
inside the step as the sample does for brevity. In host mode, a package initialized by another owner still goes through
the configured gate and synchronization; it is not destroyed by the initializer. Calls share one result: the first call supplies the
operation's cancellation, progress and gate, while later callers can cancel their own wait. A failure or cancellation is
retained by that initializer. Disposing it cancels an ongoing initialization; cleanup waits for in-flight YooAsset
operations that cannot be canceled through the API. The reachability probe is
yours: reuse a real request to a dedicated URL if "has a network interface" is not enough.

## Differences from Addressables

- `ContainsAsset<T>` can only check the address: YooAsset's manifest records addresses, not asset types, so `T` is not
  verified until the asset is loaded.
- The release gate (`AddressablesReleaseGate`) and endpoint routing are Addressables-adapter features. The
  synchronizer's `beforeRemoteUpdate` hook takes any gate, so you build one from the [release
  configuration](../app/release) yourself.
- The Addressables step runs its gate after catalog initialization; the YooAsset synchronizer runs it before loading a
  manifest or contacting the host.
- Editor-side bundle collector and address rules are project-specific and not part of this package.
