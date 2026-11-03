---
title: Диагностика и окружение
sidebar_position: 7
---

# Диагностика и окружение

Здесь собраны сервисы логирования, обработки ошибок, сведений о сборке, флагов функций, проверки версии и
аналитики. Каждый сервис регистрируется как обычная зависимость. Реализация по умолчанию есть только там,
где она однозначна.

## Логирование {#logging}

`IAppLogger.Log(level, category, message)` — одна точка вызова для любого числа `ILogOutput`. `UnityLogOutput`
пишет в `Debug.Log`, `LogWarning` и `LogError` в зависимости от уровня. Добавить файловый или удалённый
приёмник — значит зарегистрировать ещё один `ILogOutput`, не трогая ни одного места вызова.

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

## Сообщения об ошибках {#error-reporting}

`IErrorHandler.Report(ErrorContext)` — единое место для сообщения о **нефатальной** ошибке вместо того, чтобы
каждая подсистема записывала её в лог или подавляла по-своему. Стандартный `ErrorHandler` пишет её в лог и
публикует `ErrorReportedEvent` на [шине событий](messaging), поэтому аналитику, отладочный оверлей и отчёты о
сбоях можно подключить одной подпиской без изменений во всех местах вызова.

`ErrorContext` несёт `Code`, `Message`, `ErrorSeverity` (`Warning`, `Error`, `Critical`), необязательное
исключение и словарь параметров.

```csharp compile
public sealed class CrashReportBridge : IEventHandler<ErrorReportedEvent>
{
    public UniTask HandleAsync(ErrorReportedEvent message, CancellationToken cancellationToken = default)
    {
        if (message.Context.Severity == ErrorSeverity.Critical)
        {
            // Передайте исключение в сервис сбора отчётов о сбоях.
        }

        return UniTask.CompletedTask;
    }
}
```

## Сборка и окружение {#build-and-environment}

`IBuildContext` — идентичность сборки (`Version`, `BuildNumber`, `CommitHash`, `BuildDateUtc`, `Platform`,
`IsDevelopmentBuild`), а `IEnvironmentContext` говорит, на какое окружение нацелена сборка (`Development`,
`Staging`, `Production`). Пакет предоставляет классы данных; источника по умолчанию нет, потому что каждый
проект проставляет их по-своему (строка из CI, `ScriptableObject`, ...). Создавайте их при композиции.

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

`EnvironmentContextExtensions` добавляет `IsDevelopment()` и `IsProduction()`.

## Флаги функциональности {#feature-flags}

`IFeatureFlagService.IsEnabledAsync(featureId, ct)` отвечает игровому коду и UI на вопрос «включён ли флаг».
Пакет поставляет только сторону чтения: откуда берутся флаги (конфиг, удалённый конфиг, A/B-тест) — решение
проекта, оно передаётся как `IFeatureFlagSource`. Источник возвращает `null` для неизвестного ему флага;
сервис **бросает исключение** для флага, который не настроил ни один источник, поэтому опечатка не читается
молча как «выключено».

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

## Проверка версии {#version-check}

`IVersionChecker.CheckAsync` запрашивает последнюю версию по удалённому адресу и возвращает
`VersionCheckResult` (`CurrentVersion`, `LatestVersion`, `IsUpToDate`). `UnityVersionChecker(versionUrl)`
ожидает, что сервер вернёт простую строку версии вроде `1.4.0`; с пустым URL он сообщает, что версия
актуальна. Для полноценной политики обновления клиента по окружениям, с удалённой конфигурацией релизов и
кэшированным запасным вариантом используйте [контроллер релизов](../app/release).

## Аналитика {#analytics}

Игровой код публикует события аналитики через общую шину событий. `AnalyticsService` получает опубликованные
события, реализующие `IAnalyticsEvent` (`Name`, `Parameters`), и передаёт их в `IAnalyticsProvider`:

- `AnalyticsService` — точка входа, подписывающаяся на `IAnalyticsEvent` на шине событий, поэтому любое
  событие, опубликованное с этим интерфейсом, доходит до провайдера.
- `CompositeAnalyticsProvider` передаёт вызов каждому зарегистрированному `IAnalyticsProviderBackend`. Обычные ошибки
  провайдеров возвращаются, если все они завершились ошибкой; отмена распространяется отдельно.
- `LoggingAnalyticsProvider` пишет в лог имя события вместо отправки; используйте его в development-сборках
  или пока не подключён настоящий SDK.
- `IDesignAnalyticsEvent`, `IProgressionAnalyticsEvent`, `IResourceAnalyticsEvent` и `IAdAnalyticsEvent` —
  типизированные специализации, по которым провайдеры могут выбирать способ обработки. `AnalyticsEventId` составляет
  идентификатор вида `category:action:subject:variant`.

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

Публикуйте как `IAnalyticsEvent` (или производный тип): шина сопоставляет подписчиков со статическим типом
вызова. Готовые интеграции (Firebase, AppMetrica) описаны в [GameFramework](../gameframework/overview).

## Трассировка операций {#operation-tracing}

Трассировка операций — инструмент разработки. Код трассировки для среды выполнения компилируется в редакторе
и в сборках приложения, созданных с символом `FOUNDATION_DEVELOPMENT`; в production-профилях этот символ нужно отключать.

`IOperationTracer` записывает структурированные сведения о начале, завершении и ошибках сквозных операций. Он
не зависит от `IAppLogger`, поэтому трассировку можно включать или направлять отдельно. Декораторы сообщений
(`TracingCommandBus`, `TracingQueryBus`, `TracingEventBus`) используют его, чтобы показать поток команд и
запросов в логе или отладочном оверлее.

- `LoggingOperationTracer` пишет трассировки через `IAppLogger`; `NullOperationTracer` ничего не делает.
- `OperationTraceOptions` выбирает флаги `OperationTraceCategory` для трассировки (`Command`, `Query`,
  `Event`, `Subscriber`, `Simulation`) и умеет отключать (`Mute`) отдельные имена.
- `IOperationTracer.Begin(category, operation, name)` возвращает `IOperationTraceScope`; уничтожьте его, когда
  операция закончится, и вызовите `Fail(exception)` при сбое.

`RuntimePerformanceMetrics` измеряет частоту кадров (среднюю, минимальную и максимальную за последние 3
секунды) и счётчики профайлера Unity (CPU основного и рендер-потока, GC и память, draw calls, батчи, set-pass
calls) для внутриигрового оверлея производительности. Владелец вызывает `Start`, `Tick(unscaledDeltaTime)`
каждый кадр и `Stop`. Для некоторых отсутствующих счётчиков памяти используются данные API профайлера Unity;
недоступные значения читаются как `null`. После `Start` измерения не создают выделений в управляемой памяти.

`FrameHealthAccumulator` считает кадры одного отрезка игры (средняя частота, медленные кадры, подвисания) обычными
счётчиками без выделений, а `HealthBuckets` превращает такие числа в несколько именованных диапазонов (`Fps`,
`SlowFrames`, `Hitches`, `Duration`, `RamTier`, `MemoryStepMegabytes`): аналитические провайдеры считают различные
значения параметров, поэтому сырые числа увеличили бы кардинальность события. `MemoryStepMegabytes` имеет 13 значений: от 0 до 3072 МБ с шагом 256 МБ.

`TimedGameContextScopeFactory` (Editor и development-сборки) оборачивает `IGameContextScopeFactory` и пишет в лог, как
долго построение скоупа каждого контекста занимало основной поток; метку профайлера задаёт проект.

Диагностика устройства (хранилище и общий объём памяти) — отдельный необязательный модуль:
[GameFramework](../gameframework/overview).
