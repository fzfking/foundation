---
title: Конфиги
sidebar_position: 4
---

# Конфиги

Пакет предоставляет общий API для чтения данных, подготовленных при сборке проекта. Структуру конфигураций
(улучшения, враги, здания и другие данные) проект определяет сам; API работает с любым типом `TConfig`.

```mermaid
flowchart LR
    A["Исходные ассеты<br/>(ScriptableObject)"] -->|"Редактор: ConfigBuilder<br/>проверка и экспорт"| B["Сериализованный TConfig[]<br/>один файл на группу"]
    B -->|"доставка как TextAsset"| C["IAssetProvider"]
    C --> D["SerializedConfigProvider<br/>десериализация через ISerializer"]
    D --> E["IConfigRepository"]
    E --> F["IConfigResolver<br/>игровой код"]
```

| Тип | Роль |
| --- | --- |
| `IConfigGroupRegistry` | Объявляет, какие группы существуют: `Register<TConfig>(groupId, assetId, idSelector)`. |
| `IConfigProvider` | Загружает группу в репозиторий. `SerializedConfigProvider` — вариант по умолчанию. |
| `IConfigRepository` | API чтения: `Get<T>(id)`, `GetAll<T>()`, `Contains<T>(id)`. |
| `IConfigResolver` | Гарантирует загрузку группы, затем читает: `ResolveAsync<T>(id)`, `ResolveAllAsync<T>()`. |

**Группа** содержит записи одного типа в виде сериализованного массива (`TConfig[]`), загружаемого по одному
идентификатору ресурса. `idSelector` возвращает идентификатор записи. Если в группе есть повторяющиеся
идентификаторы, загрузка завершится ошибкой.

## Настройка во время выполнения {#runtime-setup}

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Config;
using Foundation.DI;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Serialization;
using MemoryPack;

[MemoryPackable]
public sealed partial class UpgradeConfig
{
    public string Id { get; set; }

    public int Cost { get; set; }
}

public sealed class UpgradeConfigGroups : IConfigGroupInstaller
{
    public void Install(IConfigGroupRegistry registry)
        => registry.Register<UpgradeConfig>("upgrades", "config/upgrades", config => config.Id);
}

public sealed class ConfigInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<MemoryPackSerializerAdapter>(ServiceLifetime.Singleton).As<ISerializer>();
        registry.UseConfigs();

        registry.RegisterBuildCallback(resolver =>
            new UpgradeConfigGroups().Install(resolver.Resolve<IConfigGroupRegistry>()));
    }
}
```

`SerializedConfigProvider` использует `IAssetProvider` (см. [Контент](content)). Он загружает `TextAsset` по
зарегистрированному адресу, десериализует массив `TConfig[]` через `ISerializer` и добавляет его записи в
репозиторий. Одновременные запросы одной группы используют общую загрузку; дескриптор освобождается после неё.
`UseConfigs()` завершает работу провайдера при уничтожении его scope. Отмена одного вызова прекращает только его
ожидание: общая загрузка может продолжиться и зарегистрировать группу. После остановки провайдера поздний результат
освобождается без регистрации конфигов.

Регистрируйте этот набор сервисов в установщиках **каждого [игрового контекста](game-contexts)**. Тогда
контекст получает собственные конфигурации, а игровому коду не нужно проверять идентификатор контекста.

## Чтение конфигов {#reading-configs}

```csharp compile
public sealed class UpgradeShop
{
    private readonly IConfigResolver _configs;

    public UpgradeShop(IConfigResolver configs) => _configs = configs;

    public async UniTask<int> CostOfAsync(string upgradeId, CancellationToken cancellationToken)
    {
        var upgrade = await _configs.ResolveAsync<UpgradeConfig>(upgradeId, cancellationToken);
        return upgrade.Cost;
    }

    public UniTask<IReadOnlyList<UpgradeConfig>> AllAsync(CancellationToken cancellationToken)
        => _configs.ResolveAllAsync<UpgradeConfig>(cancellationToken);
}
```

После загрузки группы `IConfigRepository.Get<T>(id)` возвращает запись синхронно. Используйте его в игровом коде,
которому нужно читать уже загруженные данные без ожидания.

## Подготовка конфигураций в редакторе {#build-time-pipeline}

Инструменты редактора превращают исходные ресурсы проекта в сериализованные группы. Запустите **Content → Config →
Build All Config**. Команда **Validate Config** только проверяет данные и не создаёт файлы.

1. Проект хранит исходные данные в ассетах `ScriptableObject`, которые реализуют `IConfigAuthoringSource`. Интерфейс
   находится в сборке `Foundation.Content.Authoring`, поэтому код приложения может использовать его без ссылки
   на инструменты редактора.
2. `ConfigBuilder` находит такие ресурсы и передаёт их экспортёрам `IConfigExporter` проекта — по одному на тип
   исходных данных — и валидаторам `IConfigValidator`. `ConfigBuilder` не знает конкретных типов конфигураций,
   поэтому для новой группы не нужно менять Foundation.
3. Экспортёр возвращает `ConfigExportGroup<T>`: имя файла, данные (обычно массив `TConfig[]`) и идентификаторы
   записей. Сборщик сериализует данные через выбранный `ISerializer` и сохраняет их в
   `ConfigBuildSettings.OutputDirectory` (по умолчанию `Assets/Generated/Config`).
4. Валидаторы возвращают ошибки `ConfigBuildError`, которые отображаются в окне сборки. После успешной сборки
   можно дополнительно вызвать `IConfigBuildPostProcessor`.

Зарегистрируйте экспортёры и валидаторы проекта в `IConfigBuildInstaller`. Из сборки редактора назначьте в
`ConfigBuildEntryPoint.BuilderFactory` фабрику, которая вызывает установщик и создаёт `ConfigBuilder`:

```csharp
public sealed class MyConfigBuildInstaller : IConfigBuildInstaller
{
    public void Install(ConfigBuildRegistry registry)
    {
        registry.RegisterExporter(new UpgradesExporter());
        registry.RegisterValidator(new UpgradesValidator());
    }
}

[UnityEditor.InitializeOnLoad]
public static class MyConfigBuildSetup
{
    static MyConfigBuildSetup()
    {
        ConfigBuildEntryPoint.BuilderFactory = CreateBuilder;
    }

    private static ConfigBuilder CreateBuilder()
    {
        var registry = new ConfigBuildRegistry();
        new MyConfigBuildInstaller().Install(registry);
        return new ConfigBuilder(new ConfigBuildSettings(), registry);
    }
}
```

Сгенерированные файлы затем нужно доставить как ассеты, чьи адреса совпадают с `assetId` соответствующей
группы.

### Двоичный формат {#the-binary-format}

Конвейер не зависит от библиотеки сериализации. `ConfigExportGroup.Serialize` принимает `ISerializer`, а
сборка выбирает его в таком порядке:

1. `ConfigBuildSettings.Serializer`, если проект его задал. Так проект выбирает свой формат; он должен совпадать
   с форматом, который использует `ISerializer` в игровой сборке.
2. Адаптер сериализатора, установленный в проекте. Адаптеры MemoryPack и MessagePack сами сообщают о себе в
   `ConfigSerializers` из небольшой редакторной сборки, поэтому при единственном установленном адаптере ничего
   настраивать не нужно.
3. Иначе сборка завершается ошибкой `NoSerializer` с указанием, что делать. Если установлено несколько
   адаптеров, она просит явный выбор: формат записи должен совпадать с форматом, который читает игра.
   *Validate* ничего не сериализует, поэтому работает и без всего этого.

```csharp
// В MyConfigBuildSetup.CreateBuilder, после регистрации в ConfigBuildRegistry:
return new ConfigBuilder(
    new ConfigBuildSettings { Serializer = new MyBinarySerializer() },
    registry);
```

Типы данных должны поддерживаться выбранным форматом. Например, MemoryPack требует атрибут `[MemoryPackable]`.
Типы данных Foundation поддерживаются адаптерами; подробнее см. [Сериализация](../adapters/serialization).

Создание, группы контента и Addressables: [Рабочее пространство контента](../editor/workspace).

Выходные пути всех групп в одной сборке должны быть уникальны, в том числе между разными источниками. Перед
проверкой и сборкой они сравниваются без учёта регистра. Совпадения отклоняются до записи файлов, чтобы результаты
не перезаписывали друг друга и сборка одинаково работала на разных файловых системах.
