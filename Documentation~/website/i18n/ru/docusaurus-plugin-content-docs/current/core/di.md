---
title: Внедрение зависимостей
sidebar_position: 1
---

# Внедрение зависимостей

Сервисы Foundation не зависят от конкретной DI-библиотеки. Установщики регистрируют сервисы через
`IServiceRegistry`, а код получает их через `IServiceResolver`. DI-адаптер связывает эти интерфейсы с контейнером
проекта. В пакете есть адаптер для [VContainer](../adapters/vcontainer). Здесь область контейнера (англ. scope)
задаёт набор регистраций и время жизни созданных сервисов.

| Тип | Роль |
| --- | --- |
| `IServiceRegistry` | Реестр, в котором установщик регистрирует типы, фабрики, экземпляры, точки входа, колбэки сборки и уничтожения. |
| `IServiceBinding` | Возвращается регистрацией. `.As<T>()` и `.AsSelf()` выбирают, как регистрация выставлена наружу. |
| `IServiceResolver` | `Resolve<T>()` и `TryResolve<T>(out T)`. |
| `IServiceScope` | Область DI-контейнера с собственным временем жизни. `CreateScope(configure)` создаёт дочернюю область с доступом к родительским регистрациям. |
| `IFeatureInstaller` | Регистрирует сервисы одной подсистемы. Корень композиции применяет список установщиков вместо одного монолитного блока. |
| `IGameObjectInjector` | Внедряет в компоненты `GameObject` сервисы, зарегистрированные в его области контейнера. |

## Время жизни регистраций {#lifetimes}

`ServiceLifetime` задаёт время жизни зависимости: `Transient` создаёт новый экземпляр при каждом запросе,
`Scoped` использует один экземпляр в пределах области контейнера, а `Singleton` — один экземпляр в области, где он
зарегистрирован, общий для её дочерних областей.

По умолчанию зарегистрированный сервис доступен по своему типу. Вызов `As<T>()` меняет тип, по которому его
можно получить; добавьте `.AsSelf()`, чтобы сохранить доступ и по конкретному типу.
Исключение составляют точки входа: `RegisterEntryPoint<T>()` регистрирует тип и все реализованные им интерфейсы.

## Установщики подсистем {#feature-installers}

Установщик подключает одну подсистему. Он получает `IServiceRegistry` и не зависит от конкретного DI-контейнера.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using UnityEngine;

public interface IDamageCalculator
{
    int Calculate(int attack, int defence);
}

public sealed class LinearDamageCalculator : IDamageCalculator
{
    public int Calculate(int attack, int defence) => Mathf.Max(0, attack - defence);
}

public sealed class CombatLog
{
    public List<string> Lines { get; } = new();
}

public sealed class CombatInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<LinearDamageCalculator>(ServiceLifetime.Singleton).As<IDamageCalculator>();
        registry.Register<CombatLog>(ServiceLifetime.Singleton);
        registry.RegisterInstance(new CombatSettings(maxRounds: 10));
    }
}

public sealed class CombatSettings
{
    public CombatSettings(int maxRounds) => MaxRounds = maxRounds;

    public int MaxRounds { get; }
}
```

## Методы регистрации `UseXxx` {#wiring-helpers-usexxx}

Для систем Foundation предусмотрены стандартные методы регистрации, поэтому корневому коду не нужно вручную
повторять настройки каждой системы. Метод `UseXxx` регистрирует систему и возвращает тот же реестр, позволяя
объединять вызовы в цепочку. Эти методы регистрируют сервисы и экземпляры, а при необходимости добавляют
действия при построении контейнера. Если нужна другая реализация, зарегистрируйте её самостоятельно. Методы `RegisterXxx` сохранены для систем, у которых нет варианта `UseXxx`.

```csharp compile
using Foundation.DI;
using Foundation.ErrorHandling;
using Foundation.FeatureFlags;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Rewards;
using Foundation.Scheduling;
using Foundation.Timing;

public sealed class CoreServicesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry
            .UseTime()
            .UseLogging()
            .UseMessaging()
            .UseErrorHandling()
            .UseScheduler()
            .UseRewards()
            .UseFeatureFlags(new StaticFlags());
    }
}

public sealed class StaticFlags : IFeatureFlagSource
{
    public Cysharp.Threading.Tasks.UniTask<bool?> TryGetAsync(
        string featureId,
        System.Threading.CancellationToken cancellationToken = default)
        => Cysharp.Threading.Tasks.UniTask.FromResult<bool?>(null);
}
```

| Помощник | Регистрирует | Требует |
| --- | --- | --- |
| `UseTime()` | `ITimeProvider`, `ITimeController` | |
| `UseLogging(outputs)` | `IAppLogger`, пишущий в указанные выходы или в консоль Unity | |
| `UseMessaging(options)` | шины событий, команд и запросов; `MessagingOptions` задаёт их [порядок](messaging#ordering) | |
| `UseErrorHandling()` | `IErrorHandler` | логгер, шину событий |
| `UseScheduler()`, `UseScopedScheduler()` | планировщик приложения или контекста; второй отменяет запланированные действия при уничтожении области | `ITimeProvider` |
| `UseStateMachine<TState>()`, `UseStateMachine<TState, TValidator>()` | конечный автомат, который получает зарегистрированные состояния из контейнера | состояния, зарегистрированные вами |
| `UseSimulation(...)`, `UseSimulationSystem<T>()` | раннер симуляции, тикающий каждый кадр, над всеми зарегистрированными системами | `UseTime()`, контейнер, умеющий разрешать коллекции |
| `UseRewards()`, `UseRewardHandler<TReward, THandler>()` | сервис наград; обработчик подключается при создании контейнера | |
| `UseFeatureFlags<TSource>()`, `UseFeatureFlags(source)` | `IFeatureFlagService` над источником | |
| `UseLocalization<TTableSource, TFormatter>(settings)`, `UseLocalization<TTableSource>(settings)` | сервис локализации и его источник; второй вариант оставляет форматтер вам | |
| `UseConfigs()`, `UseConfigGroups(installer)` | сервис конфигураций; группы можно зарегистрировать после создания контейнера | сериализатор, провайдер ресурсов |
| `UseSaveStorage(defaultFileName)`, `UseSaveOrchestrator(...)`, `UseSaveModule<T>()` | инфраструктуру сохранений (сериализатор, локальный файл, составной репозиторий); оркестратор над всеми модулями; один модуль | сериализатор, контейнер, умеющий разрешать коллекции |
| `UseUiRouter(definitions, root)` | uGUI-роутер | провайдер ассетов |
| `UseMemoryPack()`, `UseMessagePack(resolvers)` | `ISerializer` на этой библиотеке | сборку-адаптер библиотеки |
| `UseSmartFormat(configure)` | `ILocalizationFormatter` на SmartFormat | `FOUNDATION_SMARTFORMAT` |
| `UseAddressables()` | `IAssetProvider` на Addressables за очередью и спавнер объектов | логгер |

`UseSaveOrchestrator()` использует зарегистрированный `GameContextSaveProfile`. Указанные в профиле слот, версия
и миграции имеют приоритет над аргументами метода. Если профиль не зарегистрирован, используются слот по умолчанию
и переданные аргументы.

Зависимости методов регистрации перечислены в таблице и в XML-документации. Если нужный сервис не
зарегистрирован, контейнер сообщает об этом при попытке его получить.

## Необязательные зависимости {#optional-dependencies}

У части конструкторов пакета есть необязательные параметры. Если зависимость может отсутствовать, фабрика с
`TryResolve` явно задаёт запасной вариант и работает с контейнерами, которые не поддерживают значения
параметров по умолчанию:

```csharp compile
public sealed class Announcer
{
    private readonly CombatLog _log;

    public Announcer(CombatLog log, IDamageCalculator calculator)
    {
        _log = log;
        Calculator = calculator;
    }

    public IDamageCalculator Calculator { get; }

    public void Announce(string line) => _log.Lines.Add(line);
}

public sealed class AnnouncerInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register(
            resolver => new Announcer(
                resolver.Resolve<CombatLog>(),
                resolver.TryResolve<IDamageCalculator>(out var calculator) ? calculator : new LinearDamageCalculator()),
            ServiceLifetime.Singleton);
    }
}
```

## Точки входа {#entry-points}

Точка входа — сервис, жизненным циклом которого управляет DI-адаптер. `RegisterEntryPoint<T>()` регистрирует его, а
адаптер вызывает реализованные методы `Start`, `StartAsync` или `Tick` при создании области и во время её работы. При
уничтожении области асинхронный запуск отменяется, тики прекращаются, а принадлежащие ей сервисы `IDisposable`
освобождаются; общего метода `Stop` у точек входа нет.

| Интерфейс | Когда вызывается |
| --- | --- |
| `IStartableEntryPoint.Start()` | один раз, при создании области контейнера |
| `IAsyncStartableEntryPoint.StartAsync(ct)` | один раз, при создании области; операция отменяется при её уничтожении |
| `ITickableEntryPoint.Tick()` | каждый кадр |

```csharp compile
public sealed class CombatTicker : IStartableEntryPoint, ITickableEntryPoint
{
    private readonly CombatLog _log;
    private float _elapsed;

    public CombatTicker(CombatLog log) => _log = log;

    public void Start() => _log.Lines.Add("combat started");

    public void Tick() => _elapsed += Time.deltaTime;
}

public sealed class CombatTickerInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry) => registry.RegisterEntryPoint<CombatTicker>();
}
```

`RegisterBuildCallback` и `RegisterDisposeCallback` выполняют настройку после создания контейнера и очистку
перед его уничтожением. На этих методах основана регистрация обработчиков сообщений.

## Дочерние области контейнера {#child-scopes}

`IServiceScope.CreateScope` создаёт дочернюю область контейнера. Она видит регистрации родителя и может
перекрывать их. При уничтожении дочерней области освобождаются принадлежащие ей сервисы. [Игровые
контексты](../app/game-contexts) используют дочерние области.

```csharp compile
public sealed class Match
{
}

public static class ScopeExample
{
    public static Match PlayOneMatch(IServiceScope applicationScope)
    {
        using var matchScope = applicationScope.CreateScope(registry => registry.Register<Match>(ServiceLifetime.Scoped));
        return matchScope.Resolve<Match>();
    }
}
```

## Фабрики {#factories}

`IFactory<T>` и `IFactory<TParam, T>` — интерфейсы фабрик. `ResolvedFactory<T>` — базовый класс для фабрики,
которая создаёт объекты во время работы и получает зависимости через резолвер области контейнера:

```csharp compile
public sealed class Enemy
{
    public Enemy(IDamageCalculator calculator) => Calculator = calculator;

    public IDamageCalculator Calculator { get; }
}

public sealed class EnemyFactory : ResolvedFactory<Enemy>
{
    public EnemyFactory(IServiceResolver resolver) : base(resolver)
    {
    }

    public override Enemy Create() => new Enemy(Resolver.Resolve<IDamageCalculator>());
}

public sealed class EnemyFactoryInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
        => registry.Register<EnemyFactory>(ServiceLifetime.Singleton).As<IFactory<Enemy>>();
}
```

## Внедрение в MonoBehaviour {#injecting-monobehaviours}

`MonoBehaviour`, которому нужны сервисы, реализует `IServiceResolverClient`. Инжектор области контейнера
вызывает `Bind` у таких компонентов после создания `GameObject`. Так зависимости внедряются во всплывающие окна,
открытые [UI-роутером](../ui/router), и экземпляры из [пула префабов](../systems/pooling). У каждой области свой
инжектор.

```csharp compile
public sealed class EnemyView : MonoBehaviour, IServiceResolverClient
{
    private IDamageCalculator _calculator;

    public void Bind(IServiceResolver resolver) => _calculator = resolver.Resolve<IDamageCalculator>();

    public int Preview(int attack, int defence) => _calculator.Calculate(attack, defence);
}
```

При использовании VContainer собственные представления проекта могут по-прежнему использовать `[Inject]`.
Компоненты Foundation (`FoundationButton`, `LocalizedText` и другие) используют `IServiceResolverClient`, чтобы
не зависеть от конкретного контейнера.

## Написание другого адаптера {#writing-another-adapter}

Адаптер для Zenject, Reflex или Microsoft.Extensions.DependencyInjection должен реализовать интерфейсы
`IServiceRegistry`, `IServiceScope`, `IGameObjectInjector` и `IGameContextScopeFactory`, а также
поддерживать три интерфейса точек входа.
`MinimalServiceScope` — упрощённая реализация в памяти для тестов и инструментов. Она поддерживает точки входа,
параметры конструктора со значениями по умолчанию и коллекции `IEnumerable<T>`, `IReadOnlyList<T>`,
`IReadOnlyCollection<T>` и массивы, включая регистрации из родительских областей. Асинхронные точки входа получают
токен отмены при уничтожении области; `Tick()` вызывает владелец. Реализация не обнаруживает циклические
зависимости, не генерирует фабрики и не внедряет зависимости в `GameObject`. Для выпускаемой игры используйте
полноценный контейнер, например VContainer.

`MinimalServiceScope` освобождает созданные им экземпляры `IDisposable` с временем жизни singleton и scoped при
уничтожении области, которой они принадлежат. Transient-экземпляры он не отслеживает: если transient реализует
`IDisposable`, область его не освобождает, и это делает создатель. Transient, владеющий ресурсом, стоит освобождать
там, где он создан, или регистрировать с временем жизни, которым владеет область.

Коллекции не кэшируются. Каждое разрешение `IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>` или `T[]`
создаёт новый массив и заново разрешает все подходящие регистрации. Singleton- и scoped-сервисы возвращаются теми же
экземплярами, transient создаётся заново. Считайте массив снимком, а не живым представлением.
