---
title: Быстрый старт
sidebar_position: 2
---

# Быстрый старт

В этом руководстве мы соберём минимальное приложение с VContainer: зарегистрируем одну подсистему и добавим
точку входа, которая будет обращаться к ней через команды, запросы и события.

Предварительные условия: установлены пакет, UniTask и VContainer (см. [Установку](installation)).

## 1. Корневая область контейнера {#1-the-root-scope}

Компонент сцены наследуется от `VContainerRootScope<TEntryPoint>` и перечисляет установщики подсистем проекта.
`PrebindActions` регистрирует то, что ожидают найти все подсистемы, например шины сообщений.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Infrastructure.VContainer;
using Foundation.Lifecycle;
using Foundation.Messaging;
using UnityEngine;

public sealed class GameScope : VContainerRootScope<GameEntryPoint>
{
    protected override IReadOnlyList<IFeatureInstaller> FeatureInstallers { get; } = new IFeatureInstaller[]
    {
        new GoldInstaller(),
    };

    protected override void PrebindActions(IServiceRegistry registry) => registry.RegisterMessaging();
}
```

Создайте сцену с пустым GameObject и добавьте к нему компонент `GameScope`.

## 2. Подсистема {#2-a-feature}

Подсистема состоит из своих типов и установщика. Установщик получает `IServiceRegistry`, поэтому никогда не
упоминает VContainer.

```csharp compile
public sealed class GoldWallet
{
    public int Total { get; private set; }

    public void Add(int amount) => Total += amount;
}

public readonly struct AddGoldCommand
{
    public AddGoldCommand(int amount) => Amount = amount;

    public int Amount { get; }
}

public readonly struct GetGoldQuery
{
}

public readonly struct GoldChangedEvent
{
    public GoldChangedEvent(int total) => Total = total;

    public int Total { get; }
}

public sealed class AddGoldCommandHandler : ICommandHandler<AddGoldCommand>
{
    private readonly GoldWallet _wallet;
    private readonly IEventBus _events;

    public AddGoldCommandHandler(GoldWallet wallet, IEventBus events)
    {
        _wallet = wallet;
        _events = events;
    }

    public async UniTask HandleAsync(AddGoldCommand command, CancellationToken cancellationToken = default)
    {
        _wallet.Add(command.Amount);
        await _events.PublishAsync(new GoldChangedEvent(_wallet.Total), cancellationToken);
    }
}

public sealed class GetGoldQueryHandler : IQueryHandler<GetGoldQuery, int>
{
    private readonly GoldWallet _wallet;

    public GetGoldQueryHandler(GoldWallet wallet) => _wallet = wallet;

    public UniTask<int> HandleAsync(GetGoldQuery query, CancellationToken cancellationToken = default)
        => UniTask.FromResult(_wallet.Total);
}

public sealed class GoldInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<GoldWallet>(ServiceLifetime.Singleton);
        registry.RegisterCommandHandler<AddGoldCommand, AddGoldCommandHandler>();
        registry.RegisterQueryHandler<GetGoldQuery, int, GetGoldQueryHandler>();
    }
}
```

## 3. Точка входа {#3-the-entry-point}

`RootEntryPoint` запускается после создания DI-контейнера. Метод `RunAsync` задаёт последовательность запуска
приложения. В полноценной игре она обычно завершается входом в [игровой контекст](../app/game-contexts).

```csharp compile
public sealed class GoldLogger : IEventHandler<GoldChangedEvent>
{
    public UniTask HandleAsync(GoldChangedEvent message, CancellationToken cancellationToken = default)
    {
        Debug.Log($"Gold changed: {message.Total}");
        return UniTask.CompletedTask;
    }
}

public sealed class GameEntryPoint : RootEntryPoint
{
    private readonly ICommandBus _commands;
    private readonly IQueryBus _queries;
    private readonly IEventBus _events;

    public GameEntryPoint(ICommandBus commands, IQueryBus queries, IEventBus events)
    {
        _commands = commands;
        _queries = queries;
        _events = events;
    }

    protected override async UniTask RunAsync(CancellationToken cancellation)
    {
        using var subscription = _events.Subscribe(new GoldLogger());

        await _commands.SendAsync(new AddGoldCommand(10), cancellation);
        var total = await _queries.QueryAsync<GetGoldQuery, int>(new GetGoldQuery(), cancellation);

        Debug.Log($"Gold: {total}");
    }
}
```

Нажмите Play. В консоли появятся `Gold changed: 10`, а затем `Gold: 10`.

## Что произошло {#what-just-happened}

- Корневой компонент создал DI-контейнер, применил `GoldInstaller` и запустил `GameEntryPoint` как асинхронную точку входа.
- `RegisterCommandHandler` зарегистрировал обработчик и подключил его к шине команд при создании контейнера.
  При уничтожении контейнера обработчик отключается.
- `GoldLogger` подписывается вручную в `RunAsync` и отписывается при выходе из блока `using`. Обработчик,
  зарегистрированный через реестр, оставался бы подключён до уничтожения контейнера.
- Точка входа зависит от шин, а не от `GoldWallet`: вызывающие общаются через сообщения.

## Что дальше {#where-to-go-next}

- Добавить сохранение: [Сохранения](../app/save).
- Запустить игру в отдельной области DI-контейнера: [Игровые контексты](../app/game-contexts).
- Показать экран: [UI-роутер](../ui/router).
- Поставить экран загрузки перед всем этим: [Конвейер
  загрузки](../concepts/app-composition#1-boot-before-the-container-exists).
