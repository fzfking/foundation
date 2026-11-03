---
title: VContainer
sidebar_position: 2
---

# VContainer

`Foundation.Infrastructure.VContainer` — эталонная реализация [DI-порта](../core/di). Адаптер подключается
автоматически при установленных `jp.hadashikick.vcontainer` и UniTask. Всё в Foundation регистрируется через
`Foundation.DI` (`IServiceRegistry`, `IFeatureInstaller`); адаптер преобразует эти регистрации в вызовы
VContainer, и больше ничто в пакете на VContainer не ссылается.

| Тип | Роль |
| --- | --- |
| `VContainerRootScope<TEntryPoint>` | `LifetimeScope`, применяющий ваши установщики и запускающий `RootEntryPoint`. |
| `VContainerServiceRegistry` | Оборачивает `IContainerBuilder`. Регистрирует `IServiceResolver`, `IServiceScope` и `IGameObjectInjector` в каждой области контейнера и связывает интерфейсы точек входа с механизмами `IStartable`/`IAsyncStartable`/`ITickable` VContainer. |
| `VContainerServiceScope` | Оборачивает `IObjectResolver`. Метод `CreateScope` создаёт дочернюю область, которая владеет созданным для неё контейнером и освобождает его при уничтожении области. |
| `VContainerGameObjectInjector` | Вызывает `InjectGameObject` VContainer, затем `IServiceResolverClient.Bind` для собственных компонентов пакета (`FoundationButton`, `LocalizedText`, ...). Собственные представления проекта по-прежнему используют `[Inject]`. |
| `VContainerGameContextScopeFactory` | Дочерняя область контейнера [игрового контекста](../app/game-contexts) (`IGameContextScopeFactory`). |
| `FoundationContainerBuilderExtensions` | `builder.AsServiceRegistry()`, `resolver.AsServiceScope()`, `installer.Install(builder)` и `RegisterMessaging`, `RegisterCommandHandler`, `RegisterQueryHandler`, `RegisterScheduler` прямо на `IContainerBuilder`. |
| `VContainerRegistryExtensions` | `registry.ToContainerBuilder()` возвращает исходный `IContainerBuilder`. |

## Корневая область контейнера {#a-root-scope}

`FeatureInstallers` перечисляет установщики, которые применяются в заданном порядке. `PrebindActions` выполняется
первым — для того, что установщики ожидают найти уже зарегистрированным (шины сообщений, планировщик, фабрика
областей игровых контекстов), и для ссылок на сцену, которые контейнер создать не может.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Audio;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.Infrastructure.VContainer;
using Foundation.Lifecycle;
using Foundation.Messaging;
using Foundation.Pooling;
using Foundation.Scheduling;

public sealed class GameEntryPoint : RootEntryPoint
{
    private readonly IGameContextController _contexts;

    public GameEntryPoint(IGameContextController contexts) => _contexts = contexts;

    protected override async UniTask RunAsync(CancellationToken cancellation)
        => await _contexts.EnterStartupContextAsync(cancellation);
}

public sealed class GameScope : VContainerRootScope<GameEntryPoint>
{
    protected override IReadOnlyList<IFeatureInstaller> FeatureInstallers { get; } = new IFeatureInstaller[]
    {
        new PoolingInstaller(),
        new AudioInstaller(new IAudioChannelDefinition[] { new AudioChannelDefinition("ui") }),
    };

    protected override void PrebindActions(IServiceRegistry registry)
    {
        registry.RegisterMessaging();
        registry.RegisterScheduler();
        registry.Register<VContainerGameContextScopeFactory>(ServiceLifetime.Singleton).As<IGameContextScopeFactory>();
    }
}
```

`VContainerGameContextScopeFactory` принимает собственный `IObjectResolver` VContainer, который VContainer
всегда предоставляет. Для планировщика проекту также нужно зарегистрировать `ITimeProvider`, а для точки входа —
`IGameContextController` и его зависимости. Эти регистрации в примере не показаны.

## Смешивание с обычным VContainer {#mixing-with-plain-vcontainer}

Если приложение уже создаёт `IContainerBuilder`, установщики Foundation можно подключить и без корневой области контейнера.
Вызов `installer.Install(builder)` устанавливает `IFeatureInstaller`, а `builder.AsServiceRegistry()` даёт
доступ к тому же построителю через `IServiceRegistry`. Если установщику нужна специфичная возможность VContainer
(например, регистрация параметров, компонентов или фабрик), получите исходный построитель через
`registry.ToContainerBuilder()`. Такой установщик будет зависеть от VContainer; остальные могут использовать
только `IServiceRegistry`.

## Замена контейнера {#swapping-the-container}

Чтобы использовать другую DI-библиотеку, напишите адаптер ([как](overview#a-di-adapter-concretely)).
