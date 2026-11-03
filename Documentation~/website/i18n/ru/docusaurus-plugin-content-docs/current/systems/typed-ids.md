---
title: Типизированные идентификаторы
sidebar_position: 3
---

# Типизированные идентификаторы

Типизированные идентификаторы не дают случайно передать между системами `string` или `Guid` не того назначения.
Например, следующий код не скомпилируется:

```csharp
audio.Play(screenId);        // ScreenId не является AudioEventId
router.Open(audioEventId);   // и наоборот
```

Сборка: `Foundation.TypedIds` (без зависимостей). Инструменты редактора: `Foundation.TypedIds.Editor`.

## Два вида идентификаторов, которые никогда не смешиваются {#two-kinds-of-id-never-mixed}

| | Статический идентификатор контента | Идентификатор времени выполнения |
| --- | --- | --- |
| Идентифицирует | авторские определения: аудиособытие, экран, производство | то, что создаётся во время игры: сущности, транзакции сохранения, сетевые записи |
| Основан на | строковом **ключе** (`"ui.button_click"`) | `Guid` |
| Объявляется с помощью | `[StableId]` | `[RuntimeId]` |
| Создаётся | дизайнером или `AudioEventId.From("ui.button_click")` | `SaveTransactionId.New(guidGenerator)` |
| Стабилен при переименованиях | да: ключ отделён от отображаемого имени | неприменимо |
| Хранится в сохранениях и конфигах | да | да |

Стабильный идентификатор — это **ключ, а не хеш и не GUID**: он читаем в диффах git, логах и YAML, а
переименование отображаемого имени определения никогда его не затрагивает. Смена самого *ключа* — миграция
контента. Валидация обнаруживает отсутствующие ссылки в проверяемых ассетах, но не всякую смену ключа и не
мигрирует существующие сохранения. GUID создают только для идентичностей, порождаемых во время выполнения:
они не требуют согласования, не описывают контент и не сортируются по времени создания.

## Объявление идентификаторов {#declaring-ids}

```csharp compile
using System.Collections.Generic;
using Foundation.TypedIds;

[StableId]
public partial struct ScreenId
{
}

[StableId]
public partial struct ProductionId
{
}

[RuntimeId]
public partial struct SaveTransactionId
{
}

public static class TypedIdExample
{
    public static void Run()
    {
        var id = ScreenId.From("hud.main");        // или new ScreenId("hud.main")
        var key = id.Value;                        // "hud.main" (для значения по умолчанию — пустая строка)
        var empty = default(ScreenId).IsEmpty;     // true — значение по умолчанию пустое
        var lookup = new Dictionary<ScreenId, string> { [id] = key };

        var guids = new GuidGenerator();           // внедрите IGuidGenerator; в тесте задайте фиксированные GUID
        var transaction = SaveTransactionId.New(guids);
        UnityEngine.Debug.Log($"{id} {empty} {lookup.Count} {transaction}");
    }
}
```

Объявляйте `partial struct`, а не `readonly` (Unity не сериализует readonly-поля; сгенерированные члены —
readonly). Остальное добавляет Roslyn-генератор исходников, так что шаблонного кода на каждый идентификатор
нет. У стабильного идентификатора есть `From(key)`, конструктор, `Value`, `IsEmpty`, `None`, `==`, `CompareTo` и
отображение для отладчика. Идентификатор времени выполнения хранит `Guid` и вместо `From(key)` предоставляет
`New(generator)`, `TryParse` и конструктор с `Guid`.

- Для стабильных идентификаторов равенство, хеширование и сортировка основаны на порядковом сравнении ключей (ordinal). `null` и `""` — один
  и тот же идентификатор «ничего», поэтому только что десериализованное значение по умолчанию равно `default`.
- При обычном использовании нет упаковки, рефлексии и аллокаций; `Dictionary<TId, T>` использует собственный
  `IEquatable` структуры.
- **Неявных и явных преобразований** в `string` и из неё, а также между типами идентификаторов **нет**. В этом
  и смысл.
- `StableIdKey.IsValid(key)` определяет синтаксис ключа (ASCII-буквы, цифры, `. _ - / :`, не более 128
  символов), а `StableIdKey.StableHash(key)` — не зависящий от процесса FNV-1a на случай, когда хэш нужно
  сохранить. Значение `GetHashCode()` **не** стабильно между процессами.

В Foundation объявлены `AudioEventId`, `AudioChannelId`, `PoolId`, `BadgeId` и `FeedbackId`. Идентификаторы
вашей игры (`ProductionId`, `ScreenId`, `ItemId`, `SequenceId`, ...) объявляйте рядом с кодом соответствующей
подсистемы.

## Генератор {#the-generator}

Unity применяет генератор исходного кода ко всем сборкам проекта, включая игровые.

| Диагностика | Значение |
| --- | --- |
| `TID001` | Тип идентификатора не `partial`. |
| `TID002` | Тип идентификатора `readonly`. |
| `TID003` | Тип идентификатора обобщённый или вложенный. |

`UnityEngine.SerializeField` добавляется, только если объявляющая сборка ссылается на UnityEngine; в сборке
без движка идентификатор по-прежнему работает в коде, но не сериализуется Unity. Если такая сборка использует
MemoryPack, вызовите `TypedIdRegistration.RegisterMemoryPackFormatters()` самостоятельно.

## Сериализация {#serialization}

- **Unity.** `[StableId]` сериализуется как одно строковое поле `value` и проходит круговой путь через
  `JsonUtility`, ScriptableObject, префабы и сцены. `[RuntimeId]` Unity не сериализуется (Unity не умеет
  сериализовать `Guid`); создавайте его во время работы и сохраняйте через MemoryPack.
- **MemoryPack.** Когда на MemoryPack есть ссылка, генератор создаёт `{Id}MemoryPackFormatter` и
  `<Assembly>.Generated.TypedIdRegistration.RegisterMemoryPackFormatters()`. Регистрация выполняется
  автоматически в редакторе и игровых сборках, но **не** в сборках, где статические инициализаторы не выполняются
  при загрузке (сборки горячего обновления HybridCLR): вызывайте её сами при запуске. Аннотируйте поле DTO:

  ```csharp
  [MemoryPackable]
  public partial class AudioBinding
  {
      [MemoryPackAllowSerialize] public AudioEventId Event;   // ID с постоянным ключом: используется форматтер
      public SaveTransactionId Transaction;                   // ID времени выполнения: 16 байт, аннотация не нужна
  }
  ```

  **Ограничение.** Генератор MemoryPack не видит члены, созданные другим генератором, поэтому считает
  структуру `[StableId]` неуправляемой. Если идентификатор объявлен в *той же сборке*, что и содержащий его
  тип `[MemoryPackable]`, компиляция падает (`CS8377`). Поместите общие идентификаторы в сборку, на которую
  ссылаются DTO (для идентификаторов Foundation это происходит автоматически), или объявите поле DTO как
  `string` и предоставьте типизированное свойство. У идентификаторов времени выполнения такого ограничения нет.

## Инструменты редактора {#editor-tooling}

- **Каталог.** Сообщите редактору, что является определениями, через `IStableIdProvider` (находится
  автоматически) или, для определений-ScriptableObject, наследуйтесь от `ScriptableObjectStableIdProvider<TId,
  TAsset>`:

  ```csharp
  public sealed class ItemIdProvider : ScriptableObjectStableIdProvider<ItemId, ItemDefinition>
  {
      protected override void Describe(ItemDefinition asset, List<StableIdEntry> entries)
          => entries.Add(new StableIdEntry(asset.Id, asset.DisplayName, "Items", asset));
  }
  ```

- **Валидация.** **Foundation → Typed IDs → Validate All IDs** проверяет для каждого типа идентификатора:
  пустые или некорректные ключи, **дубликаты** ключей и каждое сериализованное поле в ScriptableObject и
  префабах под `Assets/`, которое ссылается на идентификатор, которого нет ни в одном определении
  (переименованное или удалённое определение).
- Каталог и валидатор — данные для выбора идентификатора. Пакет не поставляет UI выбора, поэтому проект,
  которому нужен выпадающий список с поиском, пишет `PropertyDrawer` поверх `StableIdCatalog`.

## Чего нет {#not-included}

Идентификаторы не интернируются и не хэшируются в целые числа (поиск стоит одного строкового хэша; на горячих
путях, например в аудио, идентификатор разрешается один раз на запрос). Нет автоматической миграции при смене
ключа, нет идентификаторов объектов сцены и ничего про сеть.
