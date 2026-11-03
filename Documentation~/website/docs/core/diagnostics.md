---
title: Diagnostics and environment
sidebar_position: 7
---

# Diagnostics and environment

Small contracts for logging, error reporting, build identity, feature flags, version checks and analytics. Each has a
default implementation (or none, where a default would be a guess) and is registered like any other service.

## Logging

`IAppLogger.Log(level, category, message)` is one call site for any number of `ILogOutput`s. `UnityLogOutput` writes to
`Debug.Log`, `LogWarning` and `LogError` by level. Adding a file or remote sink means registering another `ILogOutput`,
not touching any call site.

```csharp compile
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Analytics;
using Foundation.Build;
using Foundation.DI;
using Foundation.ErrorHandling;
using Foundation.FeatureFlags;
using Foundation.Lifecycle;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Versioning;

public sealed class DiagnosticsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<IAppLogger>(
            _ => new AppLogger(new ILogOutput[] { new UnityLogOutput() }),
            ServiceLifetime.Singleton);
        registry.Register<ErrorHandler>(ServiceLifetime.Singleton).As<IErrorHandler>();
    }
}

public sealed class SaveWriter
{
    private readonly IAppLogger _logger;
    private readonly IErrorHandler _errors;

    public SaveWriter(IAppLogger logger, IErrorHandler errors)
    {
        _logger = logger;
        _errors = errors;
    }

    public void Write()
    {
        try
        {
            _logger.Log(LogLevel.Info, "Save", "Writing slot 'auto'.");
        }
        catch (Exception exception)
        {
            _errors.Report(new ErrorContext("save_write_failed", "Slot 'auto' failed to write.", ErrorSeverity.Warning, exception));
        }
    }
}
```

## Error reporting

`IErrorHandler.Report(ErrorContext)` is the one place to report a **non-fatal** error instead of each subsystem logging
or swallowing it differently. The default `ErrorHandler` logs it and publishes an `ErrorReportedEvent` on the [event
bus](messaging), so analytics, a debug overlay or crash reporting subscribe once instead of every call site knowing
about them.

`ErrorContext` carries a `Code`, a `Message`, an `ErrorSeverity` (`Warning`, `Error`, `Critical`), an optional exception
and a parameter dictionary.

```csharp compile
public sealed class CrashReportBridge : IEventHandler<ErrorReportedEvent>
{
    public UniTask HandleAsync(ErrorReportedEvent message, CancellationToken cancellationToken = default)
    {
        if (message.Context.Severity == ErrorSeverity.Critical)
        {
            // Forward to your crash reporter.
        }

        return UniTask.CompletedTask;
    }
}
```

## Build and environment

`IBuildContext` is the build's identity (`Version`, `BuildNumber`, `CommitHash`, `BuildDateUtc`, `Platform`,
`IsDevelopmentBuild`) and `IEnvironmentContext` says which environment the build targets (`Development`, `Staging`,
`Production`). The package ships the data holders; there is no default source, because every project stamps these
differently (a CI-injected string, a `ScriptableObject`, ...). Construct them at composition time.

```csharp compile
public sealed class BuildInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance<IBuildContext>(
            new BuildContext(
                UnityEngine.Application.version,
                UnityEngine.Application.platform.ToString(),
                UnityEngine.Debug.isDebugBuild));

        registry.RegisterInstance<IEnvironmentContext>(new EnvironmentContext(GameEnvironment.Production));
    }
}

public sealed class BuildReporter : IStartableEntryPoint
{
    private readonly IAppLogger _logger;
    private readonly IBuildContext _build;
    private readonly IEnvironmentContext _environment;

    public BuildReporter(IAppLogger logger, IBuildContext build, IEnvironmentContext environment)
    {
        _logger = logger;
        _build = build;
        _environment = environment;
    }

    public void Start() => _logger.Log(LogLevel.Info, "Build", BuildContextText.Describe(_build, _environment));
}
```

`EnvironmentContextExtensions` adds `IsDevelopment()` and `IsProduction()`.

## Feature flags

`IFeatureFlagService.IsEnabledAsync(featureId, ct)` answers "is this flag on" for gameplay and UI code. The package
ships the read side only: where flags come from (config, remote config, A/B test) is a project decision, supplied as an
`IFeatureFlagSource`. A source returns `null` for a flag it does not know; the service **throws** for a flag no source
configured, so a typo does not silently read as "off".

```csharp compile
public sealed class DictionaryFlagSource : IFeatureFlagSource
{
    private readonly Dictionary<string, bool> _flags;

    public DictionaryFlagSource(Dictionary<string, bool> flags) => _flags = flags;

    public UniTask<bool?> TryGetAsync(string featureId, CancellationToken cancellationToken = default)
        => UniTask.FromResult(_flags.TryGetValue(featureId, out var value) ? value : (bool?)null);
}

public sealed class FlagsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance<IFeatureFlagSource>(
            new DictionaryFlagSource(new Dictionary<string, bool> { ["new-onboarding"] = true }));
        registry.Register<FeatureFlagService>(ServiceLifetime.Singleton).As<IFeatureFlagService>();
    }
}

public sealed class OnboardingSelector
{
    private readonly IFeatureFlagService _flags;

    public OnboardingSelector(IFeatureFlagService flags) => _flags = flags;

    public UniTask<bool> UseNewOnboardingAsync(CancellationToken cancellationToken)
        => _flags.IsEnabledAsync("new-onboarding", cancellationToken);
}
```

## Version check

`IVersionChecker.CheckAsync` asks a remote endpoint for the latest version and returns a `VersionCheckResult`
(`CurrentVersion`, `LatestVersion`, `IsUpToDate`). `UnityVersionChecker(versionUrl)` expects the endpoint to return a
plain version string such as `1.4.0`; with an empty URL it reports up to date. For a full client update policy per
environment, with a remote release configuration and a cached fallback, use the [release controller](../app/release).

## Analytics

The game raises analytics as ordinary events. An event that implements `IAnalyticsEvent` (`Name`, `Parameters`) is
forwarded by `AnalyticsService` to an `IAnalyticsProvider`:

- `AnalyticsService` is an entry point that subscribes to `IAnalyticsEvent` on the event bus, so any event published
  with that interface reaches the provider.
- `CompositeAnalyticsProvider` sends each call to every registered `IAnalyticsProviderBackend`. It reports ordinary
  backend failures if every backend fails; cancellation propagates separately.
- `LoggingAnalyticsProvider` logs the event name instead of sending it; use it in development builds or until a real SDK
  is wired up.
- `IDesignAnalyticsEvent`, `IProgressionAnalyticsEvent`, `IResourceAnalyticsEvent` and `IAdAnalyticsEvent` are typed
  specializations backends can switch on. `AnalyticsEventId` composes a `category:action:subject:variant` identifier.

```csharp compile
public sealed class LevelCompletedEvent : IAnalyticsEvent
{
    public LevelCompletedEvent(int level) => Parameters = new Dictionary<string, object> { ["level"] = level };

    public string Name => "level_completed";

    public IReadOnlyDictionary<string, object> Parameters { get; }
}

public sealed class AnalyticsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<LoggingAnalyticsProvider>(ServiceLifetime.Singleton).As<IAnalyticsProviderBackend>();
        registry.Register(
            resolver => new CompositeAnalyticsProvider(new[] { resolver.Resolve<IAnalyticsProviderBackend>() }),
            ServiceLifetime.Singleton).As<IAnalyticsProvider>();
        registry.RegisterEntryPoint<AnalyticsService>();
    }
}

public sealed class LevelFinisher
{
    private readonly IEventBus _events;

    public LevelFinisher(IEventBus events) => _events = events;

    public UniTask FinishAsync(int level) => _events.PublishAsync<IAnalyticsEvent>(new LevelCompletedEvent(level));
}
```

Publish as `IAnalyticsEvent` (or a type derived from it): the bus matches subscribers against the static type of the
call. Concrete backends (Firebase, AppMetrica) are in [GameFramework](../gameframework/overview).

## Operation tracing

Operation tracing is a development tool. Its runtime source is compiled for the Editor and for players built with
`FOUNDATION_DEVELOPMENT`; production profiles should omit that symbol.

`IOperationTracer` records structured begin, end and failure of cross-cutting operations. It is independent of
`IAppLogger`, so tracing can be toggled or routed separately. The messaging decorators (`TracingCommandBus`,
`TracingQueryBus`, `TracingEventBus`) use it to show command and query flow in a log or debug overlay.

- `LoggingOperationTracer` writes traces through `IAppLogger`; `NullOperationTracer` is a no-op.
- `OperationTraceOptions` selects the `OperationTraceCategory` flags to trace (`Command`, `Query`, `Event`,
  `Subscriber`, `Simulation`) and can `Mute` individual names.
- `IOperationTracer.Begin(category, operation, name)` returns an `IOperationTraceScope`; dispose it when the operation
  ends and call `Fail(exception)` on failure.

`RuntimePerformanceMetrics` samples frame rate (average, minimum and maximum over the last 3 seconds) and Unity profiler
counters (CPU main and render thread, GC and memory, draw calls, batches, set-pass calls) for an in-game performance
overlay. The owner calls `Start`, `Tick(unscaledDeltaTime)` each frame and `Stop`. Missing memory counters can use Unity
profiler API fallbacks; unavailable values read as `null`. Sampling allocates nothing after `Start`.

`FrameHealthAccumulator` counts the frames of one stretch of play (average rate, slow frames, stalls) with plain counters and
no allocation, and `HealthBuckets` turns such numbers into a few named ranges (`Fps`, `SlowFrames`, `Hitches`, `Duration`,
`RamTier`, `MemoryStepMegabytes`): analytics providers count distinct parameter values, so raw numbers would multiply an
event's cardinality. This bucket has 13 possible values: 0 through 3072 MB in 256 MB steps.

`TimedGameContextScopeFactory` (Editor and development builds) wraps an `IGameContextScopeFactory` and logs how long
building each context's scope blocked the main thread, under a profiler marker the project names.

Device diagnostics (storage and total memory) are a separate optional module:
[GameFramework](../gameframework/overview).
