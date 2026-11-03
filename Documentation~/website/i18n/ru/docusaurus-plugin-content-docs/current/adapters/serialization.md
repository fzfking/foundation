---
title: Сериализация
sidebar_position: 3
---

# Сериализация

Сохранения, конфигурации и данные обучения сериализуются через один интерфейс. Какой двоичный формат использовать,
решает проект.

```csharp
public interface ISerializer
{
    byte[] Serialize<T>(T value);
    T Deserialize<T>(byte[] bytes);
    object Deserialize(Type type, byte[] bytes);
}
```

Готовой реализации нет. Выберите адаптер и зарегистрируйте его один раз — например, вместе с установщиками
[сохранений](../app/save) и [конфигураций](../app/config).

| Адаптер | Сборка | Включается |
| --- | --- | --- |
| MemoryPack | `Foundation.Infrastructure.MemoryPack` | `FOUNDATION_MEMORYPACK` (вручную) |
| MessagePack | `Foundation.Infrastructure.MessagePack` | `FOUNDATION_MESSAGEPACK` (вручную) |

Обе библиотеки распространяются через NuGet, а `versionDefines` Unity не обнаруживает такие зависимости.
Установите пакеты через NuGetForUnity, затем выберите **Foundation → Bootstrap New Project → Finish Setup**.
Команда проверит наличие сборок и добавит нужный символ компиляции.

```csharp compile
using Foundation.DI;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Serialization;

public sealed class SerializationInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
        => registry.Register<MemoryPackSerializerAdapter>(ServiceLifetime.Singleton).As<ISerializer>();
}
```

## MemoryPack {#memorypack}

`MemoryPackSerializerAdapter` — один из вариантов сериализатора. Его редакторная сборка регистрирует адаптер в
`ConfigSerializers`, поэтому, если установлен только MemoryPack, экспорт конфигураций и локализации записывает
данные в этом формате без дополнительной настройки. Подробнее см. [Двоичный формат сборки](../app/config#the-binary-format).

Для собственных типов используйте стандартный атрибут `[MemoryPackable]`. Типы данных Foundation — `TutorialConfig`,
`TutorialSnapshot`, `TutorialScriptConfig` и его узлы, `LocalizationTableData` — этого атрибута не имеют. Для них
`FoundationMemoryPackFormatters` содержит форматтеры с явно заданным порядком полей. Они регистрируются:

- при создании адаптера;
- в `[RuntimeInitializeOnLoadMethod]` и, в редакторе, `[InitializeOnLoadMethod]`, так что код, напрямую
  вызывающий `MemoryPackSerializer`, работает и без адаптера;
- явным вызовом `FoundationMemoryPackFormatters.Register()`, который нужен сборке горячего обновления
  HybridCLR, потому что инициализаторы загрузки там не выполняются.

## MessagePack {#messagepack}

`MessagePackSerializerAdapter` сериализует данные сохранений и конфигураций, которые определяет проект.
`FoundationMessagePackResolver` содержит написанные вручную форматтеры для типов Foundation и массивов, например
`TutorialScriptConfig[]`, который сериализуется как корневой объект. Адаптер не генерирует форматтеры и не
регистрирует их глобально: резолвер передаётся сериализатору явно. Для типов Foundation динамическая генерация
не нужна. Для собственных типов проекта подготовьте форматтеры, совместимые с AOT.

Адаптер читает данные с `MessagePackSecurity.UntrustedData`: сохранение или скачанный конфиг не являются данными,
которые программа только что сама записала. Глубина вложенности ограничена, а ключи словарей хешируются так, чтобы
специально подготовленные данные не могли исчерпать стек или замедлить словарь.

Если вызвать `new MessagePackSerializerAdapter()` без параметров, для собственных типов адаптер использует
`StandardResolver`. Он ожидает атрибуты и генерирует код во время работы. Это подходит для редактора и Mono,
но не для IL2CPP. В AOT-сборке передайте резолверы, сгенерированные для типов вашего проекта:

```csharp
new MessagePackSerializerAdapter(GeneratedResolver.Instance);
```

Сборка контента тоже умеет писать MessagePack: если это единственный установленный адаптер, сборка использует
его, а при двух установленных выбор делается через `ConfigBuildSettings.Serializer`. Адаптер и его тесты
проверены на MessagePack-CSharp 3.1.2 в отдельном .NET-проекте (настройки адаптера дополнительно проверены на 3.1.9 —
версии, которую следует использовать: более ранние выпуски затронуты опубликованными уязвимостями, в том числе
неограниченной рекурсией на пропускаемых вложенных значениях, от которой `UntrustedData` не защищает); ни адаптер, ни сборка контента на MessagePack
не запускались внутри проекта Unity.

## Написание другого сериализатора {#writing-another-serializer}

Реализуйте `ISerializer` над любой библиотекой. Типы данных пакета — простые классы с публичными полями; если
вашей библиотеке для них нужны форматтеры или атрибуты, предоставьте их в адаптере, как это делают два
адаптера выше.

Чтобы сборка контента писала этот формат, либо задайте `ConfigBuildSettings.Serializer` экземпляром, либо
поставьте небольшую редакторную сборку, как делают два адаптера: из `[InitializeOnLoadMethod]` она вызывает
`ConfigSerializers.Register("Name", () => new YourSerializer())`.
