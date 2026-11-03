---
title: Интеграции аналитики
sidebar_position: 3
---

# Интеграции аналитики

Игровой код публикует события аналитики через общую шину событий, а `AnalyticsService` передаёт их в
`IAnalyticsProvider`. Модель событий и `CompositeAnalyticsProvider` описаны в разделе
[Диагностика](../core/diagnostics#analytics). В GameFramework входят интеграции с SDK AppMetrica и Firebase.
Зарегистрируйте их как реализации `IAnalyticsProviderBackend` в составном провайдере.

## AppMetrica {#appmetrica}

`Foundation.GameFramework.Analytics.AppMetrica` требует `io.appmetrica.analytics` (UPM, OpenUPM или git URL).

| Тип | Роль |
| --- | --- |
| `AppMetricaActivation` | Единственное место, которое должно вызывать `AppMetrica.Activate`. `ActivateIfNeeded(apiKey, crashReporting, nativeCrashReporting, firstActivationAsUpdate)` идемпотентен. Ваш шлюз согласия решает, *когда* его вызвать; этот класс решает, *как*. |
| `AppMetricaAnalyticsProviderBackend` | `IAnalyticsProviderBackend`. |
| `AppMetricaUnhandledExceptionReporter` | Точка входа, дополняющая нативный перехват сбоев AppMetrica управляемыми исключениями C#, которые Unity только записала в лог. |

AppMetrica может собирать нативные отчёты о сбоях в рамках той же активации, что и аналитику; `ActivateIfNeeded`
по умолчанию включает отчёты о сбоях и нативные отчёты о сбоях. `AppMetricaUnhandledExceptionReporter` отдельно
передаёт управляемые исключения C#, которые Unity только записала в лог.

## Firebase {#firebase}

Три сборки поверх Firebase Unity SDK. Каждая компилируется только при наличии `FOUNDATION_GAMEFRAMEWORK` и своего
пакета:

| Сборка | Пакет | Define |
| --- | --- | --- |
| `Foundation.GameFramework.Firebase.Core` | `com.google.firebase.app` | `FOUNDATION_FIREBASE_CORE` |
| `Foundation.GameFramework.Analytics.Firebase` | `com.google.firebase.analytics` | `FOUNDATION_FIREBASE_ANALYTICS` |
| `Foundation.GameFramework.Crashlytics.Firebase` | `com.google.firebase.crashlytics` | `FOUNDATION_FIREBASE_CRASHLYTICS` |

`FirebaseActivationCore.EnsureReadyAsync()` — общая активация. Он проверяет сервисы Google Play (на Android при
необходимости исправляет их состояние), создаёт приложение Firebase по умолчанию один раз за процесс, возвращает
одинаковый результат всем вызывающим и записывает в лог одну ошибку запуска. Его ждут и Analytics, и Crashlytics.
Единственная ссылка между ними — сборка Core:
`Foundation.GameFramework.Crashlytics.Firebase` ссылается на неё, а не на сборку Analytics, поэтому для компиляции
Crashlytics не нужен `com.google.firebase.analytics`.

**Analytics** (`Foundation.GameFramework.Analytics.Firebase`):

- `FirebaseActivation.EnsureReadyAsync()` остаётся в этой сборке для совместимости исходного кода и делегирует в
  `FirebaseActivationCore`.
- `FirebaseAnalyticsCollection.SetEnabledAsync(bool)` включает или выключает сбор. Так применяется согласие
  игрока.
- `FirebaseAnalyticsProviderBackend` — реализация провайдера. Пока сбор выключен, она ничего не отправляет.
- `FirebaseEventFormat` заменяет недопустимые символы, добавляет букву в начало имени, если нужно, ограничивает длину
  40 символами и меняет зарезервированные префиксы. Он не проверяет все зарезервированные имена событий: Firebase может
  проигнорировать такое имя и записать диагностическое событие `error`.

**Crashlytics** (`Foundation.GameFramework.Crashlytics.Firebase`):

- `FirebaseCrashlyticsCollection.SetEnabledAsync(bool)` применяет согласие к Crashlytics. Он вызывает
  `FirebaseActivationCore` напрямую, минуя `FirebaseActivation`. При включённом сборе он сообщает о нативных сбоях и
  исключениях, записанных Unity в лог. Чтобы отправлять необработанные управляемые исключения как критические сбои,
  нужна Firebase Unity SDK версии 10.4.0 или новее и настройка
  `Crashlytics.ReportUncaughtExceptionsAsFatal = true` при инициализации.

Тесты и CI репозитория не устанавливают эти пакеты, а определение сборки решает только вопрос компиляции. Проверьте
набор пакетов, который поставляете, на целевой платформе.

## Подключение после согласия пользователя {#putting-backends-behind-a-consent-gate}

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

Если проект запускает несколько SDK, их можно передать в `AnalyticsSdkGate`: каждый реализует `IAnalyticsSdk`
(`IsStarted`, `Start`, `SetSendingEnabled`), а гейт запускает их только после согласия (`StartIfGranted`) и применяет
последующее изменение (`Apply`): отзыв согласия останавливает то, что SDK отправляют сами, а не только события,
которые отслеживает игра. Ошибка SDK передаётся обработчику, а сам SDK пропускается, поэтому один сбой не мешает
остальным запускаться или останавливаться.

## Дополнительная настройка проекта {#project-setup-the-modules-do-not-do}

- Положите `google-services.json` из консоли Firebase в `Assets/`. Без корректной конфигурации Firebase его
  инициализация завершается неуспешно, а модули Firebase остаются неактивными; в лог записывается предупреждение.
  Это не блокирует запуск самого приложения Unity.
- По умолчанию Firebase собирает данные с запуска. Чтобы дождаться согласия, задайте
  `firebase_analytics_collection_enabled` и `firebase_crashlytics_collection_enabled` равными `false` в
  манифесте Android, затем вызовите соответствующие методы `SetEnabledAsync` из вашего шлюза согласия.
- Редакторная интеграция Firebase настраивает плагин Gradle. Для Android-сборок IL2CPP отдельно загружайте символы
  для каждой сборки, стек-трейсы которой нужно расшифровывать, с помощью Firebase CLI:

  ```text
  firebase crashlytics:symbols:upload --app=<FIREBASE_APP_ID> <PATH_TO_SYMBOLS>
  ```

  Редакторная интеграция Unity не загружает символы Android автоматически.
