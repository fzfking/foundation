---
title: Addressables
sidebar_position: 4
---

# Addressables

`Foundation.Infrastructure.Addressables` реализует [`IAssetProvider`](../app/content) на базе Unity
Addressables и добавляет офлайн-этап в [конвейер запуска приложения](../concepts/app-composition).
Адаптер подключается при наличии `com.unity.addressables`. Проверенная версия — **4.0.1** на Unity 6000.5;
проверенная ранее версия из ветки 2.x там не компилировалась из-за устаревшей перегрузки `GetInstanceID()`,
поэтому совместимость другой версии нужно проверить отдельно.

Типы Addressables используются только в этой сборке. Подсистемы и остальная часть пакета работают с
`IAssetProvider`, поэтому способ доставки контента можно заменить без изменений в их коде.

| Тип | Роль |
| --- | --- |
| `AddressablesProvider` | `IAssetProvider`. Одновременные запросы одного адреса используют общую загрузку; дескриптор освобождается вместе с последней ссылкой. |
| `AddressablesContentInitializer` | Инициализирует Addressables для офлайн-запуска и сообщает состояние, по которому экран загрузки может показать сообщение «Нет подключения. Повторная попытка…». |
| `ContentStartupBootStep` | Этап запуска: инициализация, необязательная проверка релиза и загрузка недостающего контента. |
| `AddressablesReleaseGate` | Применяет [конфигурацию релиза](../app/release) и сообщает, нужно ли сначала обновить клиент. |
| `AddressablesEndpointRouting` | Направляет запросы Addressables к каталогу контента нужного релиза. |
| `IContentHostReachabilityChecker`, `UnityWebRequestHostReachabilityChecker` | Проверка доступности, которую использует инициализатор. URL для проверки задаёте вы. |
| `AnyContentHostReachabilityChecker` | Считает контент доступным, если ответил любой из настроенных URL проверки, и сообщает об офлайне, только когда об этом сообщают все они; подходит для синхронизации через зеркала. |

Чтобы направлять попытки синхронизации через несколько источников, передайте `ContentMirrorCoordinator`; см.
[Зеркала контента](../app/content-mirroring). Координатор проверяет все зеркала в начале каждого прохода и оставляет
последнее успешное зеркало первым для следующих проходов в том же процессе; этот выбор не сохраняется между
запусками. В режиме зеркал синхронизатор использует проверку доступности только для состояния сети устройства
(`INetworkReachabilityStatusProvider.IsNetworkUnavailable`). `NotReachable` — единственный жёсткий признак офлайна:
неудачная проверка отдельного хоста сама по себе ничего не доказывает и не останавливает проход. Если проход целиком
завершился неудачно, синхронизатор возвращает настроенный endpoint Addressables и продолжает повторять через
`WaitingForConnection`; неизвестные ошибки не добавляют публичный статус, лимит повторов или terminal-исключение.
Группы и режим упаковки Addressables остаются настройкой проекта.

## Провайдер {#the-provider}

Зарегистрируйте `AddressablesProvider` как `IAssetProvider`. При необходимости оберните его очередью, которая
распределяет загрузки по кадрам.

```csharp compile
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Infrastructure.Addressables;
using Foundation.Spawning;

public sealed class AddressablesContentInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<IObjectSpawner>(_ => new AsyncObjectSpawner(AsyncObjectSpawner.DefaultIntegrationTimeMS), ServiceLifetime.Singleton);
        registry.Register(
            resolver => new QueuedAssetProvider(
                new AddressablesProvider(
                    resolver.Resolve<Foundation.Logging.IAppLogger>(),
                    resolver.Resolve<IObjectSpawner>())),
            ServiceLifetime.Singleton).As<IAssetProvider>();
    }
}
```

`InstantiateAsync` загружает префаб и создаёт объект через `IObjectSpawner` (общий покадровый бюджет
инстанцирования); экземпляр удерживает префаб и освобождает его при уничтожении.

## Запуск контента без сети {#startup-offline-first}

`ContentStartupBootStep` инициализирует Addressables по локальному или кэшированному каталогу, при необходимости
проверяет версию клиента и загружает недостающий контент. Если подключение недоступно, экран загрузки показывает
состояние ожидания. В режиме `OfflineFirst` синхронизация повторяет загрузку контента до её завершения или отмены
запуска. Ошибка самой инициализации Addressables передаётся вызывающему коду: повторный вызов не гарантирует, что
библиотека начнёт новую операцию.

`AddressablesContentInitializer` можно использовать и отдельно:

- Проверка доступности позволяет не начинать заведомо безуспешную сетевую загрузку, если нет ни соединения, ни
  локального кэша. Если кэш есть, инициализатор продолжает работу офлайн, а Addressables выбирает каталог по хешу.
- `State` и `StateChanged` позволяют показать сообщение об ожидании соединения перед инициализацией.
- Без проверки он обращается к Addressables напрямую.
- Одновременные и повторные вызовы используют один результат инициализации. Первый вызов задаёт отмену и прогресс
  общей операции; последующие могут отменить собственное ожидание. Ошибка или отмена сохраняется в инициализаторе.

Проверку релиза можно добавить в шаг запуска. Начните загружать конфигурацию заранее, чтобы к моменту проверки
результат уже был готов. Если клиенту нужно обновиться, загрузка остановится до скачивания контента для
несовместимой версии.

```csharp compile
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;
using Foundation.Release;
using UnityEngine;

public sealed class ContentBootstrap : BootstrapEntryPoint
{
    private ILoadingCurtain _curtain;

    protected override ILoadingCurtain Curtain => _curtain;

    protected override IReadOnlyList<IBootStep> CreateSteps()
    {
        var gate = CreateReleaseGate();

        return new IBootStep[]
        {
            new ContentStartupBootStep(
                new UnityWebRequestHostReachabilityChecker("https://cdn.example.com/ping"),
                _curtain,
                () => new BootMessage("No connection", "Waiting for the network..."),
                afterInitialize: cancellationToken => RequireUpdateAsync(gate)),
            new SceneBootStep(RootSceneIndex),
        };
    }

    private static AddressablesReleaseGate CreateReleaseGate()
    {
        var provider = new ReleaseConfigProvider(
            "https://example.com/release.json",
            new UnityWebRequestReleaseConfigSource(timeoutSeconds: 10),
            new FileReleaseConfigCache(Path.Combine(Application.persistentDataPath, "release.json")),
            BuiltInConfig());

        return new AddressablesReleaseGate(
            provider.LoadAsync(CancellationToken.None),
            environmentId: "production",
            contentRoot: "https://cdn.example.com/content",
            fallbackStoreUrl: "https://example.com/store",
            clientVersionText: Application.version);
    }

    private static async UniTask<BootStepOutcome> RequireUpdateAsync(AddressablesReleaseGate gate)
    {
        var result = await gate.EvaluateAsync();
        if (!result.UpdateRequired)
            return BootStepOutcome.Continue;

        Debug.LogWarning($"Update required: {result.StoreUrl}");
        return BootStepOutcome.Stop;
    }

    private static ReleaseConfig BuiltInConfig() => new()
    {
        schema = ReleaseConfig.SupportedSchema,
        client = new ClientPolicy { minimum = "1.0.0", recommended = "1.0.0", storeUrl = string.Empty },
        environments = new[]
        {
            new ReleaseEnvironment
            {
                id = "production",
                current = new ReleaseSlot { release = "built-in", minClient = "1.0.0", path = "Prod" },
            },
        },
    };
}
```

## Выбор стратегии запуска {#choosing-the-startup-strategy}

По умолчанию `ContentStartupBootStep` использует режим `OfflineFirst`; проект может выбрать другой режим.
Стратегию задаёт `ContentStartupStrategy` из `Foundation.Content`. Этот же тип использует шаг
[YooAsset](yooasset#choosing-the-startup-strategy). Обычно значение читают из настроек проекта:

| Стратегия | Что делает шаг |
| --- | --- |
| `OfflineFirst` (по умолчанию) | Инициализирует локальный или кэшированный каталог, выполняет дополнительную проверку, если она задана, и синхронизирует каталог целиком. Если на устройстве нет пригодной версии, ждёт подключения. |
| `Standard` | Вызывает `InitializeAsync`, затем выполняет дополнительную проверку, если она задана. Заранее контент не скачивается, ожидания подключения нет; ошибку инициализации получает вызывающий код. Недостающий контент загружается при первом обращении к нему. |

```csharp compile
using System.Collections.Generic;
using Foundation.Content;
using Foundation.Infrastructure.Addressables;
using Foundation.Lifecycle;

public sealed class ContentDeliverySettings
{
    public bool OfflineFirst { get; set; } = true;
}

public static class ContentBootSteps
{
    public static IBootStep Create(
        ContentDeliverySettings settings,
        IContentHostReachabilityChecker reachability,
        ILoadingCurtain curtain)
        => new ContentStartupBootStep(
            reachability,
            curtain,
            () => new BootMessage("No connection", "Waiting for the network..."),
            strategy: settings.OfflineFirst ? ContentStartupStrategy.OfflineFirst : ContentStartupStrategy.Standard);
}
```

Стратегию задаёт проект — например, в конфигурации или профиле сборки. Режим `Standard` подходит, если контент
включён в сборку игры или загружается по запросу через `EnsureLabelAsync` и другие методы Addressables.
Настройки ниже предназначены для режима `OfflineFirst`. При `Standard` оставьте *Disable Catalog Update On Start*
выключенной: Addressables должен сам искать обновлённый каталог при запуске, поскольку синхронизатор не работает.

## Настройка запуска без сети {#configuring-offline-first-content}

Работа в режиме `OfflineFirst` зависит от настроек Addressables. Ниже приведён пример конфигурации для мобильной
игры, которая доставляет контент через CDN. Измените её с учётом требований проекта к доставке и кэшированию.

**Ассет настроек (`AddressableAssetSettings`)**

| Настройка | Значение | Зачем |
| --- | --- | --- |
| Build Remote Catalog | вкл. | Игра подхватывает новый контент без обновления в магазине. |
| Bundle Local Catalog | вкл. | Каталог входит в сборку и служит отправной точкой для офлайн-запуска. Без него новая установка не сможет прочитать адреса контента. |
| Disable Catalog Update On Start | вкл. | Инициализация начинается с локального контента. Синхронизатор управляет обновлением каталога; в режиме зеркал он сначала проверяет файлы каталога на release endpoint. |
| Optimize Catalog Size | вкл. | Меньший каталог дешевле скачивать при каждом обновлении. |
| Catalog Requests Timeout | 15 с | Ограничивает ожидание недоступного сервера перед попыткой использовать кэшированный каталог. |
| Build Addressables On Player Build | вкл. | Каталог пересобирается вместе с приложением и соответствует его версии. |
| Remote Catalog Build / Load Path | `Remote.BuildPath` / `Remote.LoadPath` | Каталог лежит рядом с бандлами, которые описывает. |
| Non-Recursive Building, Contiguous Bundles | вкл. | Бандлы меньше и стабильнее; при обновлении повторно загружаются только изменившиеся данные. |

**Каждая группа с бандлами**

| Настройка | Значение | Зачем |
| --- | --- | --- |
| Build / Load Path | `Remote.BuildPath` / `Remote.LoadPath` | Контент скачивается, а не входит в сборку игры. Поместите группу на `Local.*`, чтобы поставлять её внутри сборки: тогда свежая установка сможет стартовать без сети. |
| Use Asset Bundle Cache | вкл. | Скачанные бандлы остаются на устройстве; именно этот кэш читает офлайн-запуск. |
| Use Asset Bundle CRC (also for cached bundles) | вкл. | Повреждённый кэш обнаруживается проверкой CRC и требует повторной загрузки. |
| Request Timeout / Retry Count | 30 с / 3 | Несколько попыток помогают загрузке пережить кратковременные сбои мобильной сети. |
| Bundle Naming | No hash | Имена файлов остаются стабильными. Каталог хранит хеш каждого бандла и использует его для проверки кэша, поэтому изменение обнаруживается и без нового имени файла. Настройте CDN так, чтобы он не кэшировал каталог и файл с его хешем слишком долго. |

**Профили.** Создайте отдельный профиль для каждого окружения; различаться должны только пути:

| Переменная | Production | Development |
| --- | --- | --- |
| `Remote.BuildPath` | `ServerData/Prod/[BuildTarget]` | `ServerData/Dev/[BuildTarget]` |
| `Remote.LoadPath` | `https://cdn.example.com/Prod/[BuildTarget]` | `https://cdn.example.com/Dev/[BuildTarget]` |
| `ContentState.BuildPath` | `Library/AddressablesContentState/Prod/[BuildTarget]` | `Library/AddressablesContentState/Dev/[BuildTarget]` |

Храните файл состояния контента отдельно для каждого окружения: по нему сравниваются изменения при следующей
сборке. Нельзя использовать файл одного окружения для обновления другого. Выбирайте профиль при сборке, чтобы
в релиз не попал адрес сервера разработки. После сборки загрузите содержимое `ServerData/<Env>/<Target>` в
соответствующую папку CDN **до выпуска новой версии приложения**.

**Группировка.** При запуске синхронизатор собирает из доступных каталогов адреса с зависимостями и загружает эти
зависимости. Не разделяйте контент на «обязательные» и
«необязательные» группы в надежде, что часть файлов будет пропущена. Контент, который нужно загружать позже,
разместите в отдельном каталоге или [игровом модуле](../app/game-modules).

**Чего ожидать**

- Если весь контент загружается с сервера, для первой загрузки бандлов нужна сеть. Если локальный каталог успешно
  инициализировался, синхронизация покажет состояние ожидания и продолжит работу после восстановления соединения.
- После первой синхронизации приложение может запускаться без сети из кэша, пока тот не повреждён и не удалён.
  В режиме зеркал файлы каталога на release endpoint проверяются один раз; при неудаче используется текущий каталог.
- После обновления каталога загружаются зависимости собранных адресов; при сбое загрузка переходит в ожидание.
  Синхронизатор не проверяет, что содержимое всех зеркал одинаково.

Чтобы проверить настройку, установите приложение и дождитесь первой синхронизации. Затем включите режим полёта
и запустите его снова: приложение должно открыть первый экран без состояния ожидания. После этого очистите
данные приложения и запустите его без сети. Если локальный каталог инициализировался, синхронизация должна показать
состояние ожидания; ошибка инициализации передаётся вызывающему коду.

## Время жизни {#lifetime}

Срок использования ассетов контролируется явно. Каждая загрузка возвращает дескриптор, который удерживает
ссылку на ресурс. `AssetHandleScope` освобождает сразу несколько таких дескрипторов; см. [Контент](../app/content).
Не вызывайте `Addressables.Release` из игрового кода: временем жизни ресурсов управляют провайдер и UI.
Повторные запросы одного адреса используют общую загрузку, а не создают её дубликаты.

## Группировка {#grouping}

Группы и метки Addressables описывают способ доставки ресурсов. Используйте их, чтобы упростить сопровождение
и управлять размером загрузки, а не копировать структуру пространств имён из кода. Обычно достаточно нескольких
групп по назначению: ядро, интерфейс, аудио, локализация, конфигурации и наборы контента. Не создавайте отдельную
группу для каждой подсистемы или каждого ресурса. Группы не задают структуру игровых данных; см.
[Архитектуру](../concepts/architecture#content-authoring-is-separate-from-delivery).
