---
title: Сохранения
sidebar_position: 2
---

# Сохранения

Сохранение разделено на сегменты: каждая подсистема владеет своим фрагментом и сериализует его, а оркестратор
объединяет сегменты в один блок данных. Владение отдельным сегментом — архитектурная договорённость; API не запрещает
одной подсистеме обращаться к данным другой.

| Тип | Роль |
| --- | --- |
| `IFeatureSaveModule` / `FeatureSaveModule<T>` | Сегмент одной подсистемы: `SegmentId`, `Capture()` и `Restore(snapshot)`. |
| `SaveOrchestrator` | Собирает все модули, записывает и читает блок данных. `SaveAsync`, `LoadAsync`. |
| `ISaveRepository` | Читает и записывает `SaveData`. `LocalFileSaveRepository`, `InMemorySaveRepository`, `CompositeSaveRepository`. |
| `ISaveSerializer` / `SaveSerializer` | Байтовый формат поверх любого `ISerializer` (например, [адаптера MemoryPack](../adapters/serialization)). |
| `ISaveMigration` | Один шаг обновления от версии к версии. |
| `SaveSlot` | Каждый метод репозитория принимает `slotId`; `SaveSlot.Default` — случай единственного сохранения. |
| `SaveLifecyclePump` | `MonoBehaviour`, поддерживающий сохранение актуальным, пока приложение работает. |

## Сегмент подсистемы {#a-features-segment}

Сегмент — это тип снимка, который умеет обрабатывать ваш сериализатор, плюс модуль, отображающий его в
состояние во время выполнения и обратно. Захват и восстановление синхронны и не должны трогать другие подсистемы:

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Logging;
using Foundation.Save;
using MemoryPack;

public sealed class Wallet
{
    public int Gold { get; set; }
}

[MemoryPackable]
public sealed partial class WalletSnapshot
{
    public int Gold { get; set; }
}

public sealed class WalletSaveModule : FeatureSaveModule<WalletSnapshot>
{
    private readonly Wallet _wallet;

    public WalletSaveModule(Wallet wallet) => _wallet = wallet;

    public override string SegmentId => "wallet";

    public override WalletSnapshot Capture() => new() { Gold = _wallet.Gold };

    // Restore заменяет состояние: данные до загрузки не должны в нём сохраняться.
    public override void Restore(WalletSnapshot snapshot) => _wallet.Gold = snapshot.Gold;
}
```

Правила хорошего сегмента:

- **Restore полностью восстанавливает состояние.** После `Restore` подсистема должна выглядеть в точности как
  снимок. Не оставляйте устаревших записей от состояния, которое было до этого.
- Сегмент, отсутствующий в сохранении (подсистема добавлена после записи сохранения), просто не
  восстанавливается, и подсистема сохраняет значения по умолчанию из конструктора.
- Значения `SegmentId` должны быть уникальны; дубликат — ошибка при построении оркестратора.
- Исключение в `Restore` прерывает загрузку; ошибка содержит идентификатор сегмента. Оркестратор пытается вернуть
  изменённые модули к состоянию до загрузки в обратном порядке. Если при откате тоже возникает исключение, итоговое
  состояние неизвестно.

## Подключение {#wiring}

Установщик регистрирует модуль подсистемы. Инфраструктуру сохранений обычно регистрируют один раз в области
приложения, а оркестратор — отдельно в каждом [игровом контексте](game-contexts):

```csharp compile
public sealed class SaveInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterInstance<ISaveSerializer>(new SaveSerializer(new MemoryPackSerializerAdapter()));
        registry.RegisterInstance<ILocalSaveLocation>(new PersistentDataSaveLocation());
        registry.Register(
            resolver => new LocalFileSaveRepository(
                resolver.Resolve<ILocalSaveLocation>(),
                resolver.TryResolve<IAppLogger>(out var logger) ? logger : null),
            ServiceLifetime.Singleton).As<ISaveRepositoryBackend>();
        registry.Register(
            resolver => new CompositeSaveRepository(resolver.Resolve<IReadOnlyList<ISaveRepositoryBackend>>()),
            ServiceLifetime.Singleton).As<ISaveRepository>();
    }
}

public sealed class WalletInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<Wallet>(ServiceLifetime.Singleton);
        registry.Register<WalletSaveModule>(ServiceLifetime.Singleton).As<IFeatureSaveModule>();
    }
}

public sealed class SaveOrchestratorInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register(
            resolver => new SaveOrchestrator(
                resolver.Resolve<IReadOnlyList<IFeatureSaveModule>>(),
                resolver.Resolve<ISaveRepository>(),
                resolver.Resolve<ISaveSerializer>()),
            ServiceLifetime.Singleton);
    }
}
```

`IReadOnlyList<IFeatureSaveModule>` содержит все зарегистрированные модули сохранения, доступные из текущей области контейнера.

## Сохранение и загрузка {#saving-and-loading}

```csharp compile
public sealed class SaveFlow
{
    private readonly SaveOrchestrator _save;

    public SaveFlow(SaveOrchestrator save) => _save = save;

    public async UniTask StartAsync(CancellationToken cancellationToken)
    {
        // Если сохранения ещё нет, возвращается false, а подсистемы сохраняют значения по умолчанию.
        var hadSave = await _save.LoadAsync(cancellationToken);
        UnityEngine.Debug.Log(hadSave ? "Loaded." : "New game.");
    }

    public UniTask CheckpointAsync(CancellationToken cancellationToken) => _save.SaveAsync(cancellationToken);
}
```

Что гарантирует оркестратор:

- **Операции сохранения выполняются последовательно.** Запросы могут поступать из независимых частей
  приложения (чекпоинт, сброс престижа, концовка, загрузка при старте). Внутренняя блокировка позволяет
  выполнять их по одному, и каждое захватывает свои сегменты *в момент выполнения*, так что `await
  SaveAsync()` означает, что ваше состояние сохранено.
- **Неудачная загрузка блокирует сохранение.** Если загрузка уже получила блокировку и завершилась ошибкой
  или отменой, `SaveAsync` отказывается перезаписывать этот слот до следующей завершённой загрузки того же слота,
  в том числе с результатом «сохранения нет». Отмена до получения блокировки не меняет состояние слота. Загрузка другого
  слота её не снимает. Так испорченное чтение не заменяется свежим пустым
  сохранением.
- **Время сохранения внутри одного оркестратора не убывает.** `SavedAtUtcUnixMilliseconds` не бывает меньше
  метки последнего сохранения, которое этот оркестратор загрузил или записал. `CompositeSaveRepository` выбирает
  самую новую копию по этой метке. Расхождение системных часов на разных устройствах может повлиять на выбор; сами
  метки такую разницу не исправляют.

## Версии и миграции {#versions-and-migrations}

`SaveOrchestrator` принимает текущую версию сохранения и список `ISaveMigration`. Когда загруженное сохранение
имеет более старую версию, оркестратор применяет цепочку `FromVersion → ToVersion`, пока не дойдёт до текущей,
и бросает исключение, если пути нет или шаг миграции превышает поддерживаемую версию. Сохранение **более
новой** версии, чем поддерживает сборка, отклоняется.

```csharp compile
public sealed class WalletV1ToV2 : ISaveMigration
{
    public int FromVersion => 1;

    public int ToVersion => 2;

    public SaveData Migrate(SaveData data)
    {
        // Здесь измените структуру или имена сегментов, затем верните данные с новой версией.
        return new SaveData(2, new Dictionary<string, byte[]>(data.Segments), data.SavedAtUtcUnixMilliseconds);
    }
}
```

## Репозитории {#repositories}

| Репозиторий | Применение |
| --- | --- |
| `LocalFileSaveRepository` | Файл в `Application.persistentDataPath` (через `ILocalSaveLocation`). Запись выполняется через временный файл; при замене существующего сохранения используется `File.Replace` и создаётся копия `.bak`. При первой записи временный файл перемещается на место основного без резервной копии. Файл `.bak` проверяется, только если чтение основного файла завершилось `InvalidDataException`; при успешном восстановлении повреждённый файл сохраняется как `.corrupt`. |
| `InMemorySaveRepository` | Тесты или сессия, которая не должна сохраняться. |
| `CompositeSaveRepository` | Объединяет несколько хранилищ, например локальный файл и облачную копию. При загрузке выбирает сохранение с самой поздней меткой времени. Если хранилище завершило загрузку с ошибкой и ни одно из остальных не вернуло сохранение, репозиторий выбрасывает исключение, а не сообщает, что сохранения нет. Запись считается успешной, если хотя бы одно хранилище подтвердило её. |

Успешное завершение `SaveAsync` означает, что хранилище записало данные. Если запись может быть отложена,
реализуйте `ISaveRepositoryBackendWithResult`: метод `SaveWithResultAsync` возвращает `Persisted` или `Skipped`.
`CompositeSaveRepository` сообщает об успехе, только если хотя бы одно хранилище подтвердило запись. Пропуск
облачной записи не скрывает ошибку локального хранилища. Если все хранилища вернули ошибку или пропустили запись,
операция завершается исключением. Поэтому ошибка облачного хранилища не мешает успешной локальной записи.

Восстановление из `.bak` атомарно возвращает основной файл до завершения загрузки, сохраняя резервную копию и
повреждённый файл (`.corrupt`). Повторная загрузка или перезапуск до следующего автосохранения используют
восстановленный основной файл.

Интеграция с облачным хранилищем реализует `ISaveRepositoryBackend`; см. [облачные сохранения](../gameframework/cloud-save).

### Слоты {#slots}

Каждый метод репозитория принимает `slotId` (по умолчанию `SaveSlot.Default`). Слот по умолчанию ведёт себя в
точности как проект с единственным сохранением. `PersistentDataSaveLocation` записывает слот по умолчанию в
`save.dat`, а любой другой слот — в `save-<slotId>.dat`. [Игровые контексты](game-contexts) используют слоты
как пространства имён сохранений. Необязательный параметр конструктора `defaultFileName` задаёт имя файла для
слота по умолчанию; передавать путь нельзя.

## Поддержание актуальности сохранения: `SaveLifecyclePump` {#keeping-the-save-current-savelifecyclepump}

`SaveLifecyclePump` — компонент корневой сцены, который:

- сохраняет каждые `periodicSaveSeconds` (по умолчанию 30);
- при паузе или выходе синхронно вызывает переданный обработчик `markActive`, чтобы приложение могло обновить состояние
  активности, а затем пытается асинхронно сохранить данные. Android может заморозить процесс сразу после возврата из
  колбэка, поэтому запись на диск может не успеть завершиться;
- запускает хук возобновления, когда приложение возвращается.

Активный контекст подключает свой оркестратор и колбэки через `Initialize(save, markActive, onResumed)` и
снимает их через `DetachAsync`. `PrepareForDataResetAsync` останавливает компонент сохранения перед сбросом
данных, чтобы отложенное сохранение не воскресило стёртое состояние.

`Initialize` возобновляет периодическое сохранение и сохранение при паузе или выходе после `DetachAsync` или
`PrepareForDataResetAsync`. Устаревший вызов `DetachAsync` для другого оркестратора не останавливает текущий.
Чтобы возобновить то же подключение после подготовки сброса без повторной инициализации, вызовите
`ResumePeriodicPersistence()`.
