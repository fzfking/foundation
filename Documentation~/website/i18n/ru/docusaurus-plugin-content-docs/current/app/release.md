---
title: Контроллер релизов
sidebar_position: 7
---

# Контроллер релизов

Политика версий клиента, выбор релиза по окружениям и удалённая конфигурация релизов. Пакет поставляет
контракты и логику по умолчанию; проект предоставляет свои URL, идентификаторы окружений, ссылку на магазин и
встроенную запасную конфигурацию.

При запуске он отвечает на два вопроса:

1. **Нужно ли этому клиенту обновиться?** `None`, `Recommended` или `Required`.
2. **Какой релиз контента может использовать этот клиент?** `Current`, `Previous` (для клиентов, которые ещё
   не обновились), `Incompatible` или `Unknown`, если конфигурация не описывает окружение или не публикует в нём
   релиз. Для доступного релиза: где размещён его контент.

| Тип | Роль |
| --- | --- |
| `ClientVersion` | От одного до трёх числовых компонентов, разделённых точками. Отсутствующие младшие компоненты считаются равными нулю; сравнение числовое. |
| `ReleaseConfigProvider` | Загружает конфигурацию с сервера, затем из последней успешно сохранённой копии в кэше, затем использует встроенную. Ошибка записи кэша не отменяет успешную загрузку с сервера. |
| `IReleaseManager` | `Select(environmentId, clientVersion, config)` возвращает `ReleaseSelection`. |
| `IClientUpdateService` | `Evaluate(...)` возвращает `ClientUpdateStatus`; `StoreUrl(config)` — куда отправить игрока. |
| `IContentEndpointResolver` | Возвращает папку выбранного релиза. Адаптер доставки контента, например Addressables, использует её при загрузке. |

## Конфигурация сервера {#the-server-configuration}

```json
{
  "schema": 1,
  "client": { "minimum": "1.2.0", "recommended": "1.4.0", "storeUrl": "" },
  "environments": [
    {
      "id": "production",
      "current": { "release": "2026-03-10", "minClient": "1.4.0", "path": "Prod" },
      "previous": { "release": "2026-02-20", "minClient": "1.2.0", "path": "Prod-prev" }
    }
  ]
}
```

- `client.minimum`: клиент с более ранней версией обязан обновиться. `client.recommended`: клиенту с более
  ранней версией рекомендуется обновление.
- У каждого окружения есть слот `current` и, при желании, `previous`. Слот считается опубликованным, если `release`
  не пуст. Опубликованный релиз указывает минимальную версию клиента, с которой его можно использовать (`minClient`),
  и относительный путь к папке внутри корня контента (`path`). Начальные и конечные `/` удаляются. Пустые внутренние
  сегменты, сегменты только из точек, обратная косая черта и символ `%` отклоняются; остальные символы экранируются
  как данные URL-пути.

## Проверка при запуске {#checking-at-startup}

```csharp compile
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Release;
using UnityEngine;

public sealed class ReleaseCheck
{
    public async UniTask<ReleaseDecision> CheckAsync(CancellationToken cancellationToken)
    {
        var provider = new ReleaseConfigProvider(
            "https://example.com/release.json",
            new UnityWebRequestReleaseConfigSource(timeoutSeconds: 10),
            new FileReleaseConfigCache(Path.Combine(Application.persistentDataPath, "release.json")),
            BuiltInConfig());

        var loaded = await provider.LoadAsync(cancellationToken);
        if (!ClientVersion.TryParse(Application.version, out var version))
            return new ReleaseDecision(ClientUpdateStatus.None, null);

        var selection = new ReleaseManager().Select("production", version, loaded.Config);
        var status = new ClientUpdateService("https://example.com/store").Evaluate(version, loaded.Config, selection);
        var contentFolder = selection.CanServe
            ? new ContentEndpointResolver("https://cdn.example.com/content").Resolve(selection)
            : null;

        return new ReleaseDecision(status, contentFolder);
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

public readonly struct ReleaseDecision
{
    public ReleaseDecision(ClientUpdateStatus status, string contentFolder)
    {
        Status = status;
        ContentFolder = contentFolder;
    }

    public ClientUpdateStatus Status { get; }

    public string ContentFolder { get; }
}
```

С адаптером Addressables это оборачивает `AddressablesReleaseGate`: он выбирает релиз, направляет Addressables
в папку его контента (`AddressablesEndpointRouting`) и сообщает шагу загрузки, должен ли клиент сначала
обновиться. Он подключается к `ContentStartupBootStep` как проверка `afterInitialize` ([Композиция
приложения](../concepts/app-composition)).

## Правила, которые соблюдает контроллер {#rules-the-controller-keeps}

- **Недоступный или некорректный источник не блокирует запуск сам по себе.** При ошибке загрузки
  используется следующий источник; отмена передаётся вызывающему коду. Конфигурация, которую не удалось прочитать или у которой `schema` не равна
  `ReleaseConfig.SupportedSchema`, переходит к следующему источнику (кэш, затем встроенная).
- **Некорректная строка версии не становится ограничением.** Некорректное правило версии пропускается, чтобы
  ошибка конфигурации не блокировала запуск.
- **`previous` остаётся на сервере**, пока все клиенты, которым он нужен, не обновятся из магазина. Только
  тогда его контент можно удалять. Одного предыдущего релиза достаточно: контент не хранится по версиям
  клиента.
- **Отдавайте конфигурацию и контент по HTTPS.** Конфигурация релиза определяет, из какой папки скачивается контент,
  проект может настроить проверку подписи для сборок hot-update и дополнительных AOT-метаданных. Подпись не включена
  автоматически для конфигураций, локализации и графики. По обычному HTTP злоумышленник в сети мог бы подменить то,
  что скачает клиент.
- **Продвижение — серверная операция:** опубликуйте контент релиза в папку окружения и обновите конфигурацию.
  На клиенте ничего не пересобирается.
