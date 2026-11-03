---
title: Порты и адаптеры
sidebar_position: 1
---

# Порты и адаптеры

UniTask — единственная обязательная сторонняя библиотека. Остальные точки интеграции — DI, сериализация,
анимация, реактивные потоки, сообщения, доставка ассетов и генерация случайных чисел — используют **порты**:
небольшие контракты в `Foundation`. Пакет предоставляет реализации по умолчанию там, где возможен
универсальный вариант. Необязательные сборки-**адаптеры** реализуют эти контракты поверх сторонних библиотек.

Сборка-адаптер:

- называется `Foundation.Infrastructure.<Library>`. Интеграции мобильных сервисов находятся в
  `Foundation.GameFramework.*`; два старых адаптера сохранили прежние имена: `Foundation.Haptics.Mobile` и
  `Foundation.UI.ULayout`.
- компилируется только при наличии своего пакета: `versionDefines` выставляет define `FOUNDATION_*` по пакету,
  а `defineConstraints` его требует. Библиотеку, поставляемую через NuGet, так обнаружить нельзя, поэтому её
  define выставляется вручную (**Foundation → Bootstrap New Project → Finish Setup** делает это, когда DLL на
  месте).
- изолирует большинство прямых ссылок на библиотеку в runtime-коде. Исключение — генерация типизированных ID: при
  включённом MemoryPack генератор создаёт форматтеры для сгенерированных типов ID, использующие типы MemoryPack.

## Порты {#ports}

| Порт (в `Foundation`) | По умолчанию | Адаптеры |
| --- | --- | --- |
| `IServiceRegistry`, `IServiceScope`, `IServiceResolver`, `IGameObjectInjector`, `IGameContextScopeFactory` | `MinimalServiceScope` даёт реестр, область и резолвер для тестов и инструментов; `IGameObjectInjector` и `IGameContextScopeFactory` предоставляет адаптер | [VContainer](vcontainer) |
| `IEventBus` | `EventBus` (внутрипроцессный; порядок задаёт `MessagingOptions`) | ваши |
| `IEventStream<T>`, `IReadOnlyReactiveValue<T>` | `EventStream<T>`, `ReactiveValue<T>` | ваши |
| `IMotionService` | по умолчанию ничего не зарегистрировано; `PlayerLoopMotionService` — встроенная реализация, которую нужно зарегистрировать. Действие обратной связи, которому нужна анимация, запрашивает этот сервис и выбрасывает исключение, если сервис не зарегистрирован | LitMotion (`Foundation.Infrastructure.LitMotion`) |
| `IUIViewFactory` | `UGuiViewFactory` (uGUI, создаётся `UIRouterFactory`) | [UI Toolkit, IMGUI](#ui-backends) или ваша реализация |
| `ISerializer` | нет | [MemoryPack, MessagePack](serialization) |
| `IAssetProvider` | нет | [Addressables](addressables), [YooAsset](yooasset) |
| `IRandomService` | `SystemRandomService`, `DeterministicRandomService` | ваши |
| `IGameModuleLoader` | `BuiltInGameModuleLoader`, `ManifestGameModuleLoader` | HybridCLR ([Игровые модули](../app/game-modules#hot-update-with-hybridclr)) |
| `ITimeProvider`, `ITimeController` | `SystemTimeProvider`, `UnityTimeController` | ваши |
| `IAppLogger`, `IErrorHandler` | `AppLogger`, `ErrorHandler` | ваши |
| `ISaveSerializer`, `ILocalSaveLocation` | `SaveSerializer` (над `ISerializer`), `PersistentDataSaveLocation` | ваши |
| `IAudioPlaybackBackend`, `IAudioBackend` | `UnityAudioPlaybackBackend`, `UnityAudioBackend` (и `Null*` для тестов или запуска без звука) | ваши реализации |
| `IHapticsService` | `NullHapticsService` | MobileHapticFeedback |
| `ILocalizedTextTarget` | нет: проект реализует его для своего рендерера текста | ваши |
| `ILocalizationFormatter` | нет | [SmartFormat](../app/localization#smartformat) или ваш |
| `ILocalizationTableSource` | нет | ваша реализация ([Локализация](../app/localization)) |
| `ILocalePreferenceStore` | `PlayerPrefsLocalePreferenceStore` | ваша реализация ([Локализация](../app/localization)) |
| `IAnalyticsProviderBackend` | `LoggingAnalyticsProvider` | Firebase, AppMetrica ([GameFramework](../gameframework/overview)) |
| `ISaveRepositoryBackend` | `LocalFileSaveRepository`, `InMemorySaveRepository` | облачные сохранения Google Play Games ([Облачные сохранения](../gameframework/cloud-save)) |

Интеграции мобильных сервисов — реклама, покупки, идентификация, уведомления, общий доступ, разрешения, связь и
диагностика — описаны в разделе [GameFramework](../gameframework/overview).

## Поддерживаемые UI-системы {#ui-backends}

Роутер (`UIRouter`, `UiDefinition` и `UILayer`) не зависит от конкретной UI-системы. Фабрика `IUIViewFactory`
создаёт из `UiDefinition` экземпляр `IUIViewInstance` — представление и объект, отвечающий за его уничтожение.
Роутер управляет показом и скрытием представления через `IUIView.ShowAsync` и `IUIView.HideAsync`. Поле
`IUIViewSource` в `UiDefinition` описывает источник представления; его обрабатывает выбранная фабрика:

| Реализация | Сборка | Источники | Включается |
| --- | --- | --- | --- |
| uGUI | `Foundation.UI.UGui` | `PrefabViewSource`, `AddressViewSource` | всегда |
| UI Toolkit | `Foundation.UI.Toolkit` | `UiToolkitTemplate` (UXML или дерево, построенное в коде) | `FOUNDATION_UITOOLKIT` |
| IMGUI | `Foundation.UI.Imgui` | `ImguiTemplate` (представление через `OnGUI`) | `FOUNDATION_IMGUI` |

Если проект использует другую UI-библиотеку, реализуйте и зарегистрируйте для неё `IUIViewFactory`. Изменять
`Foundation` не нужно. Каждый роутер использует одну фабрику; разные роутеры и области контейнера могут работать
с разными UI-системами.

## Установка зависимости {#installing-a-dependency}

Манифест UPM-пакета не может объявлять Git-зависимости на другие пакеты, но проект Unity может устанавливать пакеты
по Git-адресам через `Packages/manifest.json`. Добавляйте нужные библиотеки в манифест проекта, а NuGet-пакеты
устанавливайте через NuGetForUnity. В `package.json` Foundation эти библиотеки не указаны.

| Библиотека | Пакет | Проверенная версия |
| --- | --- | --- |
| UniTask (обязательно) | `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask` | 2.5.11 |
| VContainer | `https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer` | 1.19.0 |
| LitMotion | `https://github.com/annulusgames/LitMotion.git?path=src/LitMotion/Assets/LitMotion` | 2.0.2 |
| MemoryPack | NuGet `MemoryPack`, `MemoryPack.Core`, `MemoryPack.Generator` | 1.21.4 |
| MessagePack | NuGet `MessagePack`, `MessagePack.Annotations` | 3.1.9 или новее (проверено только вне Unity; у более ранних 3.1.x есть опубликованные уязвимости) |
| Addressables | `com.unity.addressables` | 4.0.1 |
| YooAsset | `com.tuyoogame.yooasset` (реестр OpenUPM `https://package.openupm.com`, область пакета `com.tuyoogame.yooasset`) | 3.0.5 |
| SmartFormat | NuGet `SmartFormat` | 3.6.1 |
| HybridCLR | `https://github.com/focus-creative-games/hybridclr_unity.git` | 8.15.0 (ветка релизов с рантаймом для Unity 6000.5) |
| uLayout | `https://github.com/pokeblokdude/uLayout.git` | 1.8.1 |
| MobileHapticFeedback | `https://github.com/tsyk5/MobileHapticFeedback.git?path=package/com.tsyk5.mobilehapticfeedback` | 0.5.0 |
| Unity Mobile Notifications | `com.unity.mobile.notifications` | 2.4.3 |
| Google Play Games | `com.google.play.games` (OpenUPM) | 2.3.0 |
| Firebase (app, analytics, crashlytics) | `https://github.com/RageAgainstThePixel/com.google.firebase.<module>.git#14.0.0` | 14.0.0 |
| AppMetrica | `io.appmetrica.analytics` (OpenUPM) | 6.10.0 |

**Finish Setup** включает `FOUNDATION_MEMORYPACK`, `FOUNDATION_MESSAGEPACK` и
`FOUNDATION_SMARTFORMAT`, если обнаруживает соответствующие сборки. Для UI Toolkit или IMGUI задайте
`FOUNDATION_UITOOLKIT` или `FOUNDATION_IMGUI` вручную в **Player Settings → Scripting Define Symbols**.
Адаптеры с автоматическим обнаружением подключаются при наличии своих пакетов и дополнительных символов
модулей, если они требуются. См. [Начальную настройку проекта](../editor/bootstrap).

## Как реализовать адаптер {#writing-an-adapter}

1. Реализуйте контракт. Не добавляйте типы сторонней библиотеки в публичные интерфейсы Foundation, которыми
   пользуется код ядра.
2. Предусмотрите короткий способ регистрации адаптера, например `registry.RegisterXxx()` или `IFeatureInstaller`.

### DI-адаптер подробно {#a-di-adapter-concretely}

Реализуйте `IServiceRegistry` поверх API регистрации контейнера, `IServiceScope` — поверх его резолвера с
поддержкой `CreateScope`, а `IGameObjectInjector` — поверх внедрения зависимостей в `GameObject`. После внедрения
через атрибуты контейнера вызывайте `IServiceResolverClient.Bind` у каждого такого компонента. В каждой области контейнера
регистрируйте доступные из него `IServiceResolver`, `IServiceScope` и `IGameObjectInjector`. Свяжите интерфейсы
`IStartableEntryPoint`, `IAsyncStartableEntryPoint` и `ITickableEntryPoint` с соответствующими механизмами
запуска контейнера. Фабрика `IGameContextScopeFactory` должна создавать дочернюю область контейнера, которая видит и может
перекрывать регистрации приложения, владеет своими синглтонами и управляет точками входа своего контекста.

## Почему кое-что остаётся в ядре {#why-some-things-stay-in-core}

`IEventStream<T>`/`ReactiveValue<T>`/`DisposableGroup`, `IMotionService` и `InterfaceReference<T>` позволяют не
включать реактивную библиотеку, LitMotion и вспомогательную библиотеку сериализации в публичные интерфейсы. Эти API намеренно минимальны: поток
событий, анимация одного значения и ссылка на объект по интерфейсу. Если проекту нужны более широкие возможности,
их предоставляет отдельный адаптер или сам проект.
