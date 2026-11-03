---
title: YooAsset
sidebar_position: 5
---

# YooAsset

`Foundation.Infrastructure.YooAsset` реализует [`IAssetProvider`](../app/content) на базе
[YooAsset](https://www.yooasset.com/). Сборка компилируется только при наличии `com.tuyoogame.yooasset`.
Поэтому проект, использующий только [Addressables](addressables), не зависит от YooAsset. Выберите один из двух
способов доставки ресурсов; код, который работает с `IAssetProvider`, менять не придётся.

Провайдер проверен на YooAsset 3.0.5 и Unity 6000.5 в режиме симуляции редактора: проверены общие загрузки,
освобождение ресурсов, отмена, восстановление после ошибки, создание и уничтожение объектов, предзагрузка и
освобождение провайдера. Режим host, синхронизатор офлайн-загрузки и стандартная инициализация компилируются
с API версии 3.0.5, но не проверялись на реальном CDN. Перед выпуском проверьте работу на устройстве по сценарию
из раздела [Addressables](addressables#configuring-offline-first-content).

| Тип | Роль |
| --- | --- |
| `YooAssetProvider` | `IAssetProvider`. Запросы одного адреса используют общий дескриптор YooAsset с учётом ссылок, аналогичным `AddressablesProvider`: одновременные запросы присоединяются к одной загрузке, отмена отдельного запроса освобождает его ссылку, не отменяя загрузку для остальных, а дескриптор освобождается, когда освобождается последняя ссылка. |
| `YooAssetContentInitializer` | Настройка пакета, вызывается до использования провайдера. |
| `IYooAssetContentSettings` | `PackageName`, `UseEditorSimulation`, `UseOfflinePlayerMode`. |
| `IYooAssetRemoteContentSettings` | Необязательный интерфейс, добавляющий `RemoteUrl`, `BuiltinPackageVersion` и `StartupStrategy` для работы инициализатора в режиме host. |
| `YooAssetContentSynchronizer` | Синхронизирует пакет в режиме `OfflineFirst`: сначала загружает доступную локальную версию, затем при подключении проверяет сервер на наличие обновления. |
| `IYooAssetVersionStore`, `PlayerPrefsYooAssetVersionStore` | Запоминает последнюю версию, скачанную полностью. |
| `YooAssetContentStartupBootStep` | Фаза контента в [конвейере загрузки](../concepts/app-composition) для пакета в режиме host. |
| `YooAssetPackageContext` | Содержит ссылку на пакет, который использует провайдер; инициализатор публикует его после настройки. |
| `ContentMirrorCoordinator` | Необязательный выбор зеркала и failover при офлайн-first синхронизации. |

Для повторов синхронизации через несколько удалённых источников передайте синхронизатору `ContentMirrorCoordinator`
и используйте callback для применения выбранного зеркала; см. [Зеркала контента](../app/content-mirroring).

## Настройка {#setup}

В редакторе инициализатор использует режим симуляции YooAsset, а в сборке — режим работы с локальным пакетом.
Для загрузки контента с сервера см. раздел [Офлайн-загрузка с сервера](#offline-first-content-from-a-host).

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
        // В этой конфигурации инициализатор создаётся без необязательного синхронизатора.
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

Дождитесь `InitializeAsync` до того, как что-либо начнёт загружать ассет; провайдер бросает исключение, если
пакет ещё не опубликован.

## Офлайн-загрузка с сервера {#offline-first-content-from-a-host}

Если вместо обычных настроек реализовать `IYooAssetRemoteContentSettings`, инициализатор использует режим host
YooAsset: сначала читает файлы из сборки, а загруженные файлы хранит в кэше и получает с адреса `RemoteUrl`.
Если включены режим симуляции в редакторе или локальный режим, они имеют приоритет. Поэтому один класс настроек
можно использовать и в редакторе, и в сборке.

### Выбор стратегии запуска {#choosing-the-startup-strategy}

Стратегию запуска задаёт проект через `IYooAssetRemoteContentSettings.StartupStrategy` типа
`ContentStartupStrategy` из `Foundation.Content`. Этот тип также использует шаг
[Addressables](addressables#choosing-the-startup-strategy). Читайте значение из настроек проекта:

| Стратегия | Что делает инициализатор |
| --- | --- |
| `OfflineFirst` | Описанный ниже синхронизатор. Он обязателен: без него инициализатор бросает исключение при создании. |
| `Standard` | В стандартном режиме YooAsset выполняет проверку обновления, запрашивает у сервера последнюю версию пакета и загружает её манифест. Остальные файлы скачиваются при первой загрузке. Если сервер недоступен, инициализация завершается ошибкой. Синхронизатор не нужен. |

В режиме host YooAsset обычно запрашивает версию пакета с сервера при запуске. `YooAssetContentSynchronizer` позволяет
запуститься с полной локальной версией и обновить её при доступности сервера:

1. Проверка `beforeRemoteUpdate` выполняется до загрузки манифеста и обращения к серверу. Она позволяет остановить
   запуск, если клиенту сначала нужно обновиться.
2. Синхронизатор проверяет последнюю полностью загруженную версию из `IYooAssetVersionStore`. Если её использовать
   нельзя, он пробует `BuiltinPackageVersion`. YooAsset определяет, остались ли незагруженные бандлы; неполная версия
   для офлайн-запуска не используется. Проверка опирается на каталог и состояние кэша YooAsset и не проверяет
   целостность каждого файла отдельно. Если манифеста нет в кэше, YooAsset может запросить его с сервера.
3. Полная локальная версия активируется. Если сервер доступен, синхронизатор заранее загружает новый манифест и
   недостающие бандлы и только после этого активирует новую версию. При ошибке загрузки локальная версия остаётся
   активной. При отмене загрузчик останавливается; если манифест уже изменился, восстанавливается предыдущий активный.
4. Если полной локальной версии нет, синхронизатор ждёт подключения и повторяет попытки, пока не загрузит пакет
   целиком. Состояние `WaitingForConnection` можно показать на экране загрузки.

Для первого офлайн-запуска включите полный пакет YooAsset в сборку и укажите его версию в `BuiltinPackageVersion`.
Инициализатор в режиме host настроит YooAsset на копирование встроенного манифеста в кэш. Указанная версия должна
совпадать с версией в сборке. Если пакет инициализирует другой владелец, настройку встроенной файловой системы и
кэша должен выполнить он. Перед использованием проверьте офлайн-запуск на целевом устройстве.

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

    // Замените это значение-заполнитель версией встроенного пакета YooAsset.
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

Для стратегии `Standard` создайте инициализатор без синхронизатора и передайте в шаг `null` вместо него. Можно также
оставить синхронизатор в примере выше: он не используется при этой стратегии.

Инициализатор использует пакет в течение всей сессии, поэтому зарегистрируйте его и синхронизатор в корневом
контейнере. Не создавайте их внутри шага загрузки, как сделано для краткости в примере. В режиме host, если пакет уже
инициализирован другим владельцем, он всё равно проходит настроенные проверку и синхронизацию; инициализатор его не уничтожает.
Вызовы используют один результат: первый задаёт отмену, прогресс и проверку общей операции, а последующие могут
отменить собственное ожидание. Ошибка или отмена сохраняется в инициализаторе. При освобождении инициализатора текущая операция отменяется;
очистка дожидается завершения операций YooAsset, для которых API не предоставляет отмену. Способ проверки связи
с сервером определяет проект. Если проверки наличия сетевого интерфейса недостаточно, отправляйте запрос на
отдельный URL.

## Отличия от Addressables {#differences-from-addressables}

- `ContainsAsset<T>` может проверить только адрес: манифест YooAsset хранит адреса, а не типы ассетов, поэтому
  `T` не проверяется до загрузки ассета.
- Шлюз релиза (`AddressablesReleaseGate`) и маршрутизация эндпоинтов — возможности адаптера Addressables. Хук
  `beforeRemoteUpdate` синхронизатора принимает любой шлюз, так что вы собираете его из [конфигурации
  релиза](../app/release) самостоятельно.
- Шаг Addressables выполняет проверку после инициализации каталога, а синхронизатор YooAsset — до загрузки
  манифеста и обращения к серверу.
- Правила сборщика бандлов и адресов на стороне редактора зависят от проекта и в пакет не входят.
