---
title: Content mirroring
sidebar_position: 6
---

# Content mirroring

Foundation can route remote content attempts through a configured set of mirrors and choose an order using measured
throughput. Mirrors are project configuration: Foundation does not provide CDN URLs, upload content, replicate origins,
or verify that two origins contain identical files.

The shared policy lives in `Foundation.ContentMirroring`. Its Unity adapter, `Foundation.Infrastructure.ContentMirroring`,
provides a `UnityWebRequest` probe and a `PlayerPrefs` statistics store.

## Configuration

Create a `ContentMirror` for each origin. Each entry has a stable ID and an absolute HTTP(S) base URL. The ID keys saved
speed measurements, so keep it stable when the origin remains the same. The project also supplies a small probe file at
the relative `ProbePath` on every mirror; the Unity probe downloads it with GET and accepts only a successful response
with the configured exact byte count.

```csharp
var mirrors = new[]
{
    new ContentMirror("edge-a", "https://edge-a.example/content"),
    new ContentMirror("edge-b", "https://edge-b.example/content"),
};

var configuration = new ContentMirrorConfiguration(
    mirrors,
    probePath: "mirror-probe.bin",
    probeSizeBytes: 262144,
    maximumEta: TimeSpan.FromSeconds(30),
    statisticsLifetime: TimeSpan.FromHours(24));

var coordinator = new ContentMirrorCoordinator(
    configuration,
    new UnityWebRequestContentMirrorProbe(),
    new PlayerPrefsContentMirrorStatisticsStore());
```

For mirrors that require query-based authentication, pass an optional synchronous `queryProvider` to `ContentMirror`. It
receives the final absolute URI (with its path already assembled) and returns query parameters, with or without a leading
`?`. It is applied to probe, YooAsset and Addressables URLs. A static query on the base URL is also preserved; fragments are
rejected because they are not sent in HTTP requests.

```csharp
var mirror = new ContentMirror(
    "signed-edge",
    "https://edge.example/content",
    queryProvider: uri => "token=" + Uri.EscapeDataString(GetTokenFor(uri)));
```

Optionally pass `preferredMirrorId` and `preferredMirrorMaximumEta` to the coordinator constructor to put a preferred
mirror first when its estimated download time is within that limit. The last successful mirror in the process takes
precedence over the preferred one.

The coordinator serializes its operations and probes every eligible mirror at the start of each `ExecuteAsync` call.
An explicitly excluded base URL is not probed. A saved speed measurement never skips a probe; it can only affect the
order of mirrors whose fresh probe succeeded, and a successful probe replaces the saved value. The mirror that last
completed an attempt successfully stays first for later calls in the same process when the configuration reuses the
same `ContentMirror` instance. That stickiness lives in memory only and resets when the process restarts. During an
attempt, progress estimates the remaining time. If it exceeds `MaximumEta`, the coordinator cancels the attempt token
and tries the next candidate after the attempt returns. The Addressables adapter waits for its current operation to
finish before switching mirrors, so a mirror change cannot abort an unrelated load that uses the same URL. Canceling
the caller's token ends its wait and releases its operation handle; a dependency download that has already started may
still finish in the background. If every candidate exceeds the ETA limit, the coordinator retries the first mirror in
its speed ranking once without the cutoff. This ranking is captured before the sticky and preferred priorities are
applied. Every candidate must pass a fresh probe, but the ranking may use a cached speed. If no attempt succeeds, it
returns `false` unless an attempt itself throws. The caller restores its configured baseline route (the Addressables
endpoint or the YooAsset remote URL). Addressables then keeps retrying through `WaitingForConnection`; YooAsset can
settle on a usable local version instead. A failed probe proves neither that the network is down nor that the mirror
lacks the rest of the content; it only means this probe file did not answer as expected.

Probes run sequentially by default. Pass `parallelProbes: true` as the final `ContentMirrorConfiguration` argument to
start them together and wait for all results before choosing a mirror. A custom `IContentMirrorProbe` must support
overlapping calls in this mode. Simultaneous downloads share bandwidth, so their measured speeds can differ from
sequential measurements. The Unity probe's `timeoutSeconds` applies to each request separately.

The Addressables `AnyContentHostReachabilityChecker` has an independent `parallelChecks` option. Its default is
sequential; `new AnyContentHostReachabilityChecker(probeUrls, timeoutSeconds: 2, parallelChecks: true)` starts HEAD
requests together and cancels the remaining requests after a host responds. Its timeout applies to each HEAD request,
not to the subsequent mirror probes or content downloads.

`PlayerPrefsContentMirrorStatisticsStore` persists bytes-per-second measurements by mirror ID. Replace it with another
`IContentMirrorStatisticsStore` if the project needs a different persistence policy. The stored speeds never decide
whether a mirror is probed, and they do not record the last successful mirror.

## Integrations

- **Addressables:** for the offline-first strategy, pass the coordinator to `ContentStartupBootStep` or
  `AddressablesContentSynchronizer`. At startup, the adapter checks the catalog files on the release endpoint and
  attempts a catalog update once if they are available. Mirror attempts then route bundle requests to the selected
  mirror, without duplicating a shared content-root path in the URL. A successful precheck does not guarantee that a
  later catalog request will succeed. The adapter does not abort requests globally when an attempt
  is canceled, because independent asset loads can use the same mirror URL. In mirror mode the synchronizer asks its
  `IContentHostReachabilityChecker` only whether the device reports no network
  (`INetworkReachabilityStatusProvider.IsNetworkUnavailable`). `NotReachable` is the only hard offline signal; a failed
  host probe by itself is unknown and leaves the mirror pass running. `AnyContentHostReachabilityChecker` reports
  offline only when every configured host does, and is useful when one checker covers several hosts. Without a
  coordinator, the existing single-endpoint path is unchanged. See [Addressables](../adapters/addressables).
- **YooAsset:** pass the coordinator to `YooAssetContentSynchronizer`; the initializer applies each selected mirror to
  its remote service. Its `applyMirror` callback receives `null` after an unsuccessful pass so it can restore the primary
  URL. The mirror must contain the same package version, manifest, and bundle paths. See
  [YooAsset](../adapters/yooasset).
- **HybridCLR:** `MirroredHotUpdateAssemblySource` can retry one assembly address on another mirror after loading or
  signature verification fails. It requires a verifier, mirror routing, and cache/download-size callbacks; it is not
  enabled by the standard `HybridClrGameModuleLoader` unless supplied as its assembly source. Its mirror callback receives
  `null` when the alternate-mirror pass returns `false`, allowing the caller to restore the original route. Errors or
  cancellation before that return follow their own exception paths. See
  [Game modules](game-modules#hot-update-with-hybridclr).

Mirroring is routing and retry policy, not content integrity or executable-code authenticity. Keep mirror content
consistent with the selected catalog/package and use the existing signature verifier for hot-update assemblies. Addressables
group and packing settings remain project-owned; mirroring does not require changing them.
