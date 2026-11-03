---
title: Ports and adapters
sidebar_position: 1
---

# Ports and adapters

UniTask is the only mandatory third-party library. Other integration points, such as DI, serialization, tweening,
reactive streams, messaging, asset delivery and random generation, use **ports**: small contracts in `Foundation`. The
package provides defaults where a general-purpose implementation is possible. Optional **adapter** assemblies implement
these contracts using third-party libraries.

An adapter assembly:

- is named `Foundation.Infrastructure.<Library>`. The mobile backends are under `Foundation.GameFramework.*`; two older
  ones keep their historical names: `Foundation.Haptics.Mobile` and `Foundation.UI.ULayout`.
- compiles only when its package is present: `versionDefines` sets a `FOUNDATION_*` define from the package, and
  `defineConstraints` requires it. A NuGet-delivered library cannot be detected that way, so its define is set manually
  (**Foundation → Bootstrap New Project → Finish Setup** does it once the DLLs are present).
- isolates most direct runtime references to the library. Typed ID generation is an exception: when MemoryPack is enabled, it emits MemoryPack-specific formatters for generated ID types.

## Ports

| Port (in `Foundation`) | Default | Adapters |
| --- | --- | --- |
| `IServiceRegistry`, `IServiceScope`, `IServiceResolver`, `IGameObjectInjector`, `IGameContextScopeFactory` | `MinimalServiceScope` provides the registry, scope and resolver for tests and tools; an adapter provides the other two | [VContainer](vcontainer) |
| `IEventBus` | `EventBus` (in-process; ordering from `MessagingOptions`) | yours |
| `IEventStream<T>`, `IReadOnlyReactiveValue<T>` | `EventStream<T>`, `ReactiveValue<T>` | yours |
| `IMotionService` | none registered by default; `PlayerLoopMotionService` is the built-in engine to register. A feedback action that uses a tween requests this service and throws if it is not registered | LitMotion (`Foundation.Infrastructure.LitMotion`) |
| `IUIViewFactory` | `UGuiViewFactory` (uGUI, built by `UIRouterFactory`) | [UI Toolkit, IMGUI](#ui-backends) or your own |
| `ISerializer` | none | [MemoryPack, MessagePack](serialization) |
| `IAssetProvider` | none | [Addressables](addressables), [YooAsset](yooasset) |
| `IRandomService` | `SystemRandomService`, `DeterministicRandomService` | yours |
| `IGameModuleLoader` | `BuiltInGameModuleLoader`, `ManifestGameModuleLoader` | HybridCLR ([Game modules](../app/game-modules#hot-update-with-hybridclr)) |
| `ITimeProvider`, `ITimeController` | `SystemTimeProvider`, `UnityTimeController` | yours |
| `IAppLogger`, `IErrorHandler` | `AppLogger`, `ErrorHandler` | yours |
| `ISaveSerializer`, `ILocalSaveLocation` | `SaveSerializer` (over `ISerializer`), `PersistentDataSaveLocation` | yours |
| `IAudioPlaybackBackend`, `IAudioBackend` | `UnityAudioPlaybackBackend`, `UnityAudioBackend` (and `Null*` ones for tests and headless runs) | yours |
| `IHapticsService` | `NullHapticsService` | MobileHapticFeedback |
| `ILocalizedTextTarget` | none: a project implements it for its text renderer | yours |
| `ILocalizationFormatter` | none | [SmartFormat](../app/localization#smartformat) or yours |
| `ILocalizationTableSource` | none | yours ([Localization](../app/localization)) |
| `ILocalePreferenceStore` | `PlayerPrefsLocalePreferenceStore` | yours ([Localization](../app/localization)) |
| `IAnalyticsProviderBackend` | `LoggingAnalyticsProvider` | Firebase, AppMetrica ([GameFramework](../gameframework/overview)) |
| `ISaveRepositoryBackend` | `LocalFileSaveRepository`, `InMemorySaveRepository` | Google Play Games cloud save ([Cloud save](../gameframework/cloud-save)) |

The mobile backends (ads, IAP, identity, notifications, sharing, permissions, connectivity, diagnostics) are covered
under [GameFramework](../gameframework/overview).

## UI backends

The router (`UIRouter`, `UiDefinition`, `UILayer`, the cache and back-stack rules) knows nothing about a UI technology.
It asks an `IUIViewFactory` to turn a `UiDefinition` into an `IUIViewInstance` (a view plus how to dispose it) and
drives `IUIView.ShowAsync`/`HideAsync`. A `UiDefinition` carries an `IUIViewSource` that says where the view comes from;
only its backend's factory interprets it:

| Backend | Assembly | Sources | Switched on by |
| --- | --- | --- | --- |
| uGUI | `Foundation.UI.UGui` | `PrefabViewSource`, `AddressViewSource` | always |
| UI Toolkit | `Foundation.UI.Toolkit` | `UiToolkitTemplate` (UXML or a code-built tree) | `FOUNDATION_UITOOLKIT` |
| IMGUI | `Foundation.UI.Imgui` | `ImguiTemplate` (an `OnGUI` view) | `FOUNDATION_IMGUI` |

A project that uses a different UI library implements `IUIViewFactory` against it and registers it; nothing in
`Foundation` changes. Only one factory is registered per router, but separate routers (and scopes) can use different
backends.

## Installing a dependency

A UPM package's own manifest cannot declare Git dependencies on other packages, but a Unity project can install Git URLs through
`Packages/manifest.json`. Install the required libraries in the project manifest and use NuGetForUnity for NuGet libraries.
Foundation does not declare these libraries in its own `package.json`.

| Library | Package | Version tested |
| --- | --- | --- |
| UniTask (required) | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` | 2.5.11 |
| VContainer | `https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer` | 1.19.0 |
| LitMotion | `https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion` | 2.0.2 |
| MemoryPack | NuGet `MemoryPack`, `MemoryPack.Core`, `MemoryPack.Generator` | 1.21.4 |
| MessagePack | NuGet `MessagePack`, `MessagePack.Annotations` | 3.1.9 or later (verified outside Unity only; earlier 3.1.x have published security advisories) |
| Addressables | `com.unity.addressables` | 4.0.1 |
| YooAsset | `com.tuyoogame.yooasset` (OpenUPM scoped registry `https://package.openupm.com`, scope `com.tuyoogame.yooasset`) | 3.0.5 |
| SmartFormat | NuGet `SmartFormat` | 3.6.1 |
| HybridCLR | `https://github.com/focus-creative-games/hybridclr_unity.git` | 8.15.0 (the release line with a Unity 6000.5 runtime) |
| uLayout | `https://github.com/pokeblokdude/uLayout.git` | 1.8.1 |
| MobileHapticFeedback | `https://github.com/tsyk5/MobileHapticFeedback.git?path=package/com.tsyk5.mobilehapticfeedback` | 0.5.0 |
| Unity Mobile Notifications | `com.unity.mobile.notifications` | 2.4.3 |
| Google Play Games | `com.google.play.games` (OpenUPM) | 2.3.0 |
| Firebase (app, analytics, crashlytics) | `https://github.com/RageAgainstThePixel/com.google.firebase.<module>.git#14.0.0` | 14.0.0 |
| AppMetrica | `io.appmetrica.analytics` (OpenUPM) | 6.10.0 |

**Finish Setup** enables `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` and
`FOUNDATION_SMARTFORMAT` when it finds their assemblies. Set `FOUNDATION_UITOOLKIT` or `FOUNDATION_IMGUI` manually in
**Player Settings → Scripting Define Symbols** when using those UI backends. Package-detected adapters enable themselves
when their packages and any additional module symbols are present. See [Project bootstrap](../editor/bootstrap).

## Writing an adapter

1. Implement the port. Keep the library's types out of every public signature that core code could see.
2. Give it a one-line registration (`registry.RegisterXxx()` or an `IFeatureInstaller`).

### A DI adapter, concretely

Implement `IServiceRegistry` over the container's builder, `IServiceScope` over its resolver (with `CreateScope`), and
an `IGameObjectInjector` that injects the container's own attributes and then calls `IServiceResolverClient.Bind` on
every client under the object. Register the scope's `IServiceResolver`, `IServiceScope` and `IGameObjectInjector` in
every scope. Forward `IStartableEntryPoint`, `IAsyncStartableEntryPoint` and `ITickableEntryPoint` to whatever the
container dispatches. Build `IGameContextScopeFactory` so that a child scope sees the application's registrations,
shadows them, owns its singletons and starts and stops its entry points with the scope.

## Why some things stay in core

`IEventStream<T>`/`ReactiveValue<T>`/`DisposableGroup`, `IMotionService` and `InterfaceReference<T>` replace what
would otherwise be a reactive library, LitMotion and a serialization helper in public signatures. They are deliberately minimal (subscribe and publish, one
float tween, one object field): a library that does more is an adapter or a project dependency, not something core
should re-export.
