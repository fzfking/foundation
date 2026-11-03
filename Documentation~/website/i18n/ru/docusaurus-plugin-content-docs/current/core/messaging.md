---
title: Сообщения
sidebar_position: 2
---

# Сообщения

Для команд, запросов и событий предусмотрены отдельные шины — `ICommandBus`, `IQueryBus` и `IEventBus`. Благодаря
этому игровой код не зависит от конкретной библиотеки обмена сообщениями.

| | Смысл | Обработчиков | Контракт |
| --- | --- | --- | --- |
| **Команда** | «Сделай это». Изменяет состояние. | ровно один | `ICommandBus.SendAsync<T>`, `ICommandHandler<T>` |
| **Запрос** | «Вычисли и верни это». | ровно один | `IQueryBus.QueryAsync<TQuery, TResult>`, `IQueryHandler<TQuery, TResult>` |
| **Событие** | «Это произошло». Факт в прошедшем времени. | любое число, включая ноль | `IEventBus.PublishAsync<T>`, `IEventBus.Subscribe<T>`, `IEventHandler<T>` |

Если для команды или запроса не зарегистрирован обработчик, вызов завершается ошибкой. События не могут отменить
изменение состояния: публикуйте их после него. Обычная ошибка обработчика не мешает шине вызвать остальные; если
публикация завершилась без отмены, все ошибки передаются вызывающему коду вместе. Отмена действует кооперативно: оба
режима проверяют токен перед запуском очередного обработчика, поэтому отменённая публикация не запускает следующие.
В режиме `Sequential` публикация бросает `OperationCanceledException`, если обработчик учёл токен или отмена обнаружена
перед запуском следующего обработчика. Если последний обработчик игнорирует отмену, публикация может завершиться успешно.
Без подходящих подписчиков публикация завершается, не проверяя токен. В режиме `Concurrent` обработчики могут уже выполняться, и публикация бросает исключение
сразу, не дожидаясь их; каждый запущенный обработчик должен сам учитывать токен, чтобы остановить свою работу, а отмена
не откатывает сделанное. Ошибки до и после отмены уходят в `UniTaskScheduler.UnobservedTaskException`, а не в
отменённый результат. При отмене `WaitForEventAsync` завершает ожидание и снимает подписку.

## Регистрация {#registering}

Область приложения регистрирует шины один раз. Обработчики регистрируются в области, которой принадлежат,
и отключаются при её уничтожении. Поэтому игровой контекст можно закрыть и запустить снова без старых подписок.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Messaging;

public readonly struct PurchaseUpgradeCommand
{
    public PurchaseUpgradeCommand(string upgradeId) => UpgradeId = upgradeId;

    public string UpgradeId { get; }
}

public readonly struct CanAffordQuery
{
    public CanAffordQuery(int cost) => Cost = cost;

    public int Cost { get; }
}

public readonly struct UpgradePurchasedEvent
{
    public UpgradePurchasedEvent(string upgradeId) => UpgradeId = upgradeId;

    public string UpgradeId { get; }
}

public sealed class PurchaseUpgradeCommandHandler : ICommandHandler<PurchaseUpgradeCommand>
{
    private readonly IEventBus _events;

    public PurchaseUpgradeCommandHandler(IEventBus events) => _events = events;

    public async UniTask HandleAsync(PurchaseUpgradeCommand command, CancellationToken cancellationToken = default)
    {
        // Проверьте условия, спишите стоимость, примените результат...
        await _events.PublishAsync(new UpgradePurchasedEvent(command.UpgradeId), cancellationToken);
    }
}

public sealed class CanAffordQueryHandler : IQueryHandler<CanAffordQuery, bool>
{
    public UniTask<bool> HandleAsync(CanAffordQuery query, CancellationToken cancellationToken = default)
        => UniTask.FromResult(query.Cost <= 100);
}

public sealed class UpgradesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.RegisterCommandHandler<PurchaseUpgradeCommand, PurchaseUpgradeCommandHandler>();
        registry.RegisterQueryHandler<CanAffordQuery, bool, CanAffordQueryHandler>();
    }
}
```

В области контейнера приложения:

```csharp
registry.RegisterMessaging();                          // CommandBus, QueryBus и внутрипроцессный EventBus
registry.RegisterMessaging(new MessagingOptions(...)); // то же, с выбранным порядком (см. ниже)
```

`RegisterMessaging()` регистрирует шины `ICommandBus`, `IQueryBus` и `IEventBus`, а также реестры обработчиков
`ICommandHandlerRegistry` и `IQueryHandlerRegistry`. `RegisterCommandAndQueryBuses()` регистрирует только шины
команд и запросов — этот вариант подходит проектам со своей реализацией `IEventBus`.

## Порядок {#ordering}

Порядок выполнения обработчиков и команд — настройка регистрации, `MessagingOptions`, а не свойство библиотеки под
капотом:

| Настройка | Значение | Что гарантируется |
| --- | --- | --- |
| `Events` | `Sequential` (по умолчанию) | Каждый обработчик завершается до начала следующего, поэтому следующий видит результат предыдущего. Обработчик, отписавшийся, пока выполняется предыдущий, не вызывается. Отменённая публикация не запускает следующий обработчик. |
| | `Concurrent` | Обработчики запускаются, не дожидаясь завершения предыдущих, а публикация ждёт их всех, если не отменена. Порядок завершения не гарантирован. |
| `Commands` | `Immediate` (по умолчанию) | Команда сразу уходит обработчику; отправленные за это время команды могут выполняться одновременно. |
| | `Serialized` | Одна команда за раз, в порядке отправки: команда ждёт, пока завершится предыдущая. |

```csharp
// Обработчики событий и команды, меняющие состояние, выполняются по очереди.
registry.RegisterMessaging(new MessagingOptions(EventDispatchMode.Sequential, CommandExecutionMode.Serialized));
```

Подписчики на один тип события вызываются в порядке подписки. Опубликованному типу могут соответствовать подписки на
несколько базовых типов или интерфейсов; шина группирует их по типу подписки и не гарантирует общий порядок подписок.

При `Serialized` ожидающую команду можно отменить, и тогда она не выполнится; упавшая команда передаёт очередь
следующей. Обработчик не должен делать `await` другой команды через ту же шину: она будет ждать обработчик, который
ждёт её. Отправьте её без ожидания или вызовите сервис другой фичи напрямую. Запросы только читают состояние и в
очередь не ставятся.

Упавший обработчик события не мешает следующим получить событие. Если публикация завершилась без отмены, ошибки
сообщаются вместе: одна пробрасывается с исходным стеком, несколько приходят как `AggregateException`.

## Использование шин {#using-the-buses}

```csharp compile
public sealed class UpgradeButtonPresenter
{
    private readonly ICommandBus _commands;
    private readonly IQueryBus _queries;

    public UpgradeButtonPresenter(ICommandBus commands, IQueryBus queries)
    {
        _commands = commands;
        _queries = queries;
    }

    public async UniTask<bool> TryBuyAsync(string upgradeId, int cost, CancellationToken cancellationToken)
    {
        var canAfford = await _queries.QueryAsync<CanAffordQuery, bool>(new CanAffordQuery(cost), cancellationToken);
        if (!canAfford)
            return false;

        await _commands.SendAsync(new PurchaseUpgradeCommand(upgradeId), cancellationToken);
        return true;
    }
}
```

Шина выбирает обработчик по типу, указанному при компиляции вызова, а не по фактическому типу объекта во время
выполнения. Поэтому команда не попадёт обработчику другого типа.

## Подписка на события {#subscribing-to-events}

`Subscribe` возвращает `IDisposable`; уничтожьте его, чтобы отписаться.

Публикация использует снимок обработчиков, взятый при старте. Подписка, добавленная во время публикации, и отписка
в режиме `Concurrent` действуют на следующую публикацию; в режиме `Sequential` уже отписавшийся обработчик
пропускается. Шина кэширует по одному снимку на тип события, хранит не больше 64 и очищает кэш при изменении подписок
или достижении предела.

```csharp compile
public sealed class UpgradeToast : IEventHandler<UpgradePurchasedEvent>
{
    public UniTask HandleAsync(UpgradePurchasedEvent message, CancellationToken cancellationToken = default)
    {
        // Покажите сообщение «Улучшение куплено».
        return UniTask.CompletedTask;
    }
}

public sealed class UpgradeToastController : System.IDisposable
{
    private readonly System.IDisposable _subscription;

    public UpgradeToastController(IEventBus events) => _subscription = events.Subscribe(new UpgradeToast());

    public void Dispose() => _subscription.Dispose();
}
```

### Ожидание одного события {#waiting-for-one-event}

`WaitForEventAsync` — одноразовое ожидание, которое само подписывается и отписывается, с необязательным
предикатом:

```csharp compile
public sealed class UpgradeWaiter
{
    private readonly IEventBus _events;

    public UpgradeWaiter(IEventBus events) => _events = events;

    public UniTask<UpgradePurchasedEvent> NextPurchaseOfAsync(string upgradeId, CancellationToken cancellationToken)
        => _events.WaitForEventAsync<UpgradePurchasedEvent>(e => e.UpgradeId == upgradeId, cancellationToken);
}
```

### Базовые типы и интерфейсы {#base-types-and-interfaces}

`EventBus` доставляет событие обработчикам, чей тип совместим с типом публикации. Например, обработчик
`IEventHandler<IGameplayEvent>` получит событие, опубликованное как тип, реализующий `IGameplayEvent`.
Для выбора используется статический тип `TEvent` в вызове `PublishAsync<TEvent>`.

## Ручная регистрация {#registering-by-hand}

`ICommandHandlerRegistry.RegisterHandler<T>` и `IQueryHandlerRegistry.RegisterHandler<TQuery, TResult>`
возвращают `IDisposable` для кода, регистрирующего обработчики вне установщика. Уничтожение удаляет
обработчик.

## Маркеры событий {#event-markers}

`IEventMarker` и три его подинтерфейса (`IGameplayEvent`, `IAnalyticsEvent`, `IApplicationEvent`) позволяют
подписчику или инструменту фильтровать события по виду. `IAnalyticsEvent` дополнительно несёт `Name` и
`Parameters`. Маркеры использовать необязательно.

## Трассировка {#tracing}

В редакторе и в сборках с символом `FOUNDATION_DEVELOPMENT` обёртки `TracingCommandBus`, `TracingQueryBus`
и `TracingEventBus` передают каждую операцию в `IOperationTracer` (см. [Диагностику](diagnostics#operation-tracing)).
Зарегистрируйте их при настройке шин. В релизные сборки они не входят.

## Соглашения {#conventions}

- Команда называется в повелительном наклонении (`PurchaseUpgradeCommand`), событие — в прошедшем времени
  (`UpgradePurchasedEvent`).
- Не каждый вызов метода должен быть командой. Используйте её там, где запрос пересекает границу подсистемы,
  где полезна трассировка или где отправитель не должен знать обработчик.
- UI должен читать состояние через запросы или модели для чтения, а не через внутреннее состояние другой
  подсистемы.
- Проверки, способные запретить изменение состояния, выполняйте в обработчике команды. События `BeforeX` не
  предоставляют механизма отмены изменения.
