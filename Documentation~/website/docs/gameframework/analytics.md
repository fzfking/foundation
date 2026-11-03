---
title: Analytics backends
sidebar_position: 3
---

# Analytics backends

Foundation raises analytics as ordinary events and fans them out to `IAnalyticsProviderBackend`s (see
[Diagnostics](../core/diagnostics#analytics) for the event model and `CompositeAnalyticsProvider`). This section covers
the two vendor backends that ship in GameFramework: AppMetrica and Firebase. Both are `IAnalyticsProviderBackend`s you
register next to your other backends in the same composite.

## AppMetrica

`Foundation.GameFramework.Analytics.AppMetrica` needs `io.appmetrica.analytics` (UPM, OpenUPM or a git URL).

| Type | Role |
| --- | --- |
| `AppMetricaActivation` | The only place that should call `AppMetrica.Activate`. `ActivateIfNeeded(apiKey, crashReporting, nativeCrashReporting, firstActivationAsUpdate)` is idempotent. Your consent gate decides *when* to call it; this decides *how*. |
| `AppMetricaAnalyticsProviderBackend` | The `IAnalyticsProviderBackend`. |
| `AppMetricaUnhandledExceptionReporter` | An entry point that supplements AppMetrica's native crash capture with managed C# exceptions that Unity only logged. |

AppMetrica can collect native crash reports through the same activation as analytics; `ActivateIfNeeded` enables crash
reporting and native crash reporting by default. `AppMetricaUnhandledExceptionReporter` separately forwards managed C#
exceptions that Unity only logged.

## Firebase

Three assemblies over the Firebase Unity SDK. Each compiles only when `FOUNDATION_GAMEFRAMEWORK` and its own package are
present:

| Assembly | Package | Define |
| --- | --- | --- |
| `Foundation.GameFramework.Firebase.Core` | `com.google.firebase.app` | `FOUNDATION_FIREBASE_CORE` |
| `Foundation.GameFramework.Analytics.Firebase` | `com.google.firebase.analytics` | `FOUNDATION_FIREBASE_ANALYTICS` |
| `Foundation.GameFramework.Crashlytics.Firebase` | `com.google.firebase.crashlytics` | `FOUNDATION_FIREBASE_CRASHLYTICS` |

`FirebaseActivationCore.EnsureReadyAsync()` is the shared activation. It checks and, on Android, repairs Google Play
services, creates the default Firebase app once per process, returns the same result to every caller, and logs a failed
start once. Analytics and Crashlytics both wait on it. The only reference between them is the Core assembly:
`Foundation.GameFramework.Crashlytics.Firebase` references Core and not the Analytics assembly, so Crashlytics does not
need `com.google.firebase.analytics` to compile.

**Analytics** (`Foundation.GameFramework.Analytics.Firebase`):

- `FirebaseActivation.EnsureReadyAsync()` stays in this assembly for source compatibility and forwards to
  `FirebaseActivationCore`.
- `FirebaseAnalyticsCollection.SetEnabledAsync(bool)` turns collection on or off. This is how the player's consent is
  applied.
- `FirebaseAnalyticsProviderBackend` is the backend. It sends nothing while collection is off.
- `FirebaseEventFormat` replaces unsupported characters, ensures names start with a letter, limits them to 40 characters
  and changes reserved prefixes. It does not check every reserved event name; Firebase can ignore a reserved name and log a
  diagnostic `error` event.

**Crashlytics** (`Foundation.GameFramework.Crashlytics.Firebase`):

- `FirebaseCrashlyticsCollection.SetEnabledAsync(bool)` applies consent to Crashlytics. It calls
  `FirebaseActivationCore` directly, without going through `FirebaseActivation`. It reports native crashes and
  Unity-logged exceptions when collection is enabled. Reporting uncaught managed exceptions as fatal also requires Firebase
  Unity SDK 10.4.0 or later and setting `Crashlytics.ReportUncaughtExceptionsAsFatal = true` during initialization.

The repository's tests and CI never install these packages, and an assembly definition only decides what compiles. Check
the package set you ship on your target platform.

## Putting backends behind a consent gate

```csharp compile
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Foundation.Analytics;
using Foundation.DI;
using Foundation.GameFramework.Analytics.AppMetrica;
using Foundation.GameFramework.Analytics.Firebase;
using Foundation.GameFramework.Crashlytics.Firebase;

public sealed class AnalyticsBackendsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<AppMetricaAnalyticsProviderBackend>(ServiceLifetime.Singleton).As<IAnalyticsProviderBackend>();
        registry.Register<FirebaseAnalyticsProviderBackend>(ServiceLifetime.Singleton).As<IAnalyticsProviderBackend>();
        registry.Register(
            resolver => new CompositeAnalyticsProvider(resolver.Resolve<IReadOnlyList<IAnalyticsProviderBackend>>()),
            ServiceLifetime.Singleton).As<IAnalyticsProvider>();
        registry.RegisterEntryPoint<AnalyticsService>();
        registry.RegisterEntryPoint<AppMetricaUnhandledExceptionReporter>();
    }
}

public sealed class ConsentGate
{
    private readonly string _appMetricaKey;

    public ConsentGate(string appMetricaKey) => _appMetricaKey = appMetricaKey;

    public async UniTask ApplyAsync(bool analyticsAllowed)
    {
        if (analyticsAllowed)
        {
            AppMetricaActivation.ActivateIfNeeded(_appMetricaKey);
            AppMetricaActivation.SetDataSendingEnabled(true);
        }
        else if (AppMetricaActivation.IsActivatedWithKey)
        {
            AppMetricaActivation.SetDataSendingEnabled(false);
        }

        await FirebaseAnalyticsCollection.SetEnabledAsync(analyticsAllowed);
        await FirebaseCrashlyticsCollection.SetEnabledAsync(analyticsAllowed);
    }
}
```

A project that runs several SDKs can hand them to `AnalyticsSdkGate`: each implements `IAnalyticsSdk` (`IsStarted`,
`Start`, `SetSendingEnabled`), and the gate starts them only once consent is granted (`StartIfGranted`) and applies a later
change (`Apply`): revoking stops what the SDKs send on their own, not only the events the game tracks. An SDK that throws
is reported and skipped, so one failure never keeps the others from starting or stopping.

## Project setup the modules do not do

- Put `google-services.json` from the Firebase console in `Assets/`. Without a valid Firebase configuration, Firebase
  activation fails and the Firebase modules stay inactive; a warning is logged. This does not prevent the Unity
  application from starting.
- Firebase collects at launch by default. To wait for consent, set `firebase_analytics_collection_enabled` and
  `firebase_crashlytics_collection_enabled` to `false` in the Android manifest, then call the matching `SetEnabledAsync`
  methods from your consent gate.
- The Firebase Unity editor integration can configure the Gradle plugin. For Android IL2CPP builds, upload native symbols
  separately for each build you want symbolicated, using the Firebase CLI:

  ```text
  firebase crashlytics:symbols:upload --app=<FIREBASE_APP_ID> <PATH_TO_SYMBOLS>
  ```

  The Unity editor integration does not perform this Android symbol upload automatically.
