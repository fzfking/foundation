---
title: Скриптовые последовательности
sidebar_position: 3
---

# Скриптовые последовательности

`Foundation.Sequences` выполняет сценарии с помощью UniTask: запускает действия, ждёт события, проверяет
условия и выполняет параллельные ветви. Система не зависит от UI, сообщений, DI или формата контента. Код
формирует массив узлов `SequenceNode` — вручную, из парсера или ScriptableObject — и передаёт его раннеру.
На этой системе построен [туториал](tutorial).

| Примитив | Значение |
| --- | --- |
| `action <key> ...` | Выполнить зарегистрированный обработчик действия. |
| `wait <key> ...` | Дождаться зарегистрированного обработчика ожидания (обычно «пока не произойдёт это событие»). |
| `wait_until [not] <key>` | Проверять условие каждый кадр, пока оно не выполнится. |
| `skip_if [not] <key>` | Если условие выполнено, завершить шаг штатно. Очистка всё равно выполнится. |
| `delay <seconds>` | Ждать в нескалированном времени. |
| `if` / `else` | Выполнить одну из двух ветвей в зависимости от условия. |
| `race` | Запустить все дочерние ветви параллельно; после завершения первой остальные отменяются, и раннер ждёт их остановки. |

Циклов, функций и интерпретатора выражений в системе нет. Всё, что примитивы не покрывают, — это
`ISequenceCommandHandler`, который вы предоставляете (например, `say` или `highlight` в туториале).

## Обработчики {#handlers}

Обработчики регистрируются под явными строковыми ключами в `SequenceHandlerRegistry`: действия и ожидания —
асинхронные делегаты, условия — быстрые синхронные предикаты. При повторении ключа в одной категории
регистрация завершается ошибкой. Подсистема также может выставить свои обработчики как
`ISequenceHandlerProvider`, что позволяет модулю с горячим обновлением вносить обработчики без сканирования
сборок.

```csharp compile
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using Foundation.Sequences;

public readonly struct DoorOpenedEvent
{
}

public sealed class DoorSequenceHandlers : ISequenceHandlerProvider
{
    private readonly IEventBus _events;
    private bool _doorIsOpen;

    public DoorSequenceHandlers(IEventBus events) => _events = events;

    public void Register(SequenceHandlerRegistry registry)
    {
        registry.RegisterAction("log", (context, _) =>
        {
            UnityEngine.Debug.Log($"{context.SequenceId}/{context.StepId}: {string.Join(" ", context.Arguments)}");
            return UniTask.CompletedTask;
        });

        registry.RegisterWait("door_opened", async (_, cancellationToken) =>
        {
            await _events.WaitForEventAsync<DoorOpenedEvent>(null, cancellationToken);
            _doorIsOpen = true;
        });

        registry.RegisterCondition("door_is_open", _ => _doorIsOpen);
    }
}
```

`context.Arguments` содержит аргументы узла после ключа обработчика.

## Выполнение шага {#running-a-step}

`SequenceRunner.RunStepAsync(sequenceId, stepId, stepIndex, body, cleanup, cancellationToken)` выполняет тело,
затем узлы очистки, даже если тело бросило исключение, было отменено или завершилось раньше. Очистка
выполняется с `CancellationToken.None`, поэтому ограничивайте время её выполнения и никогда не ждите в ней
ввода игрока.

Если тело завершилось с ошибкой, а очистка прошла успешно, исходное исключение тела передаётся дальше с прежним
стеком вызовов. Если ошиблись и тело, и очистка, раннер выбрасывает `AggregateException` с обеими ошибками; если
ошиблась только очистка, передаётся её исключение.

```csharp compile
public sealed class DoorTour
{
    private readonly SequenceRunner _runner;

    public DoorTour(SequenceHandlerRegistry handlers) => _runner = new SequenceRunner(handlers);

    public UniTask RunAsync(CancellationToken cancellationToken)
    {
        var body = new[]
        {
            SequenceNode.Action("log", "waiting for the door"),
            new SequenceNode { Command = "skip_if", Arguments = new[] { "door_is_open" } },
            new SequenceNode { Command = "wait", Arguments = new[] { "door_opened" } },
            new SequenceNode { Command = "delay", Arguments = new[] { "0.5" } },
            SequenceNode.Action("log", "door opened"),
        };

        var cleanup = new[] { SequenceNode.Action("log", "tour finished") };

        return _runner.RunStepAsync("door_tour", "wait_for_door", 0, body, cleanup, cancellationToken);
    }
}
```

Пользовательская команда (всё, что не `action`, `wait`, `wait_until`, `skip_if` или `delay`) передаётся
`ISequenceCommandHandler`, который вы назначили раннеру. Без него неизвестная команда бросает исключение с номером
исходной строки.

## Ветвление и гонки {#branching-and-racing}

`If` и `Race` — виды узлов. `Command` узла `If` — ключ условия (`Negated` задаёт «не»), `Children`
выполняются, когда условие выполнено, а `ElseChildren` — когда нет. Узел `Race` запускает все дочерние ветви
одновременно и завершается после завершения первой из них. Остальные ветви отменяются, а раннер ждёт их
завершения перед следующим шагом, поэтому обработчики обязаны учитывать отмену.

```csharp compile
public static class DoorBranches
{
    public static SequenceNode[] WaitForDoorOrTimeout() => new[]
    {
        new SequenceNode
        {
            Kind = SequenceNodeKind.If,
            Command = "door_is_open",
            Negated = true,
            Children = new[]
            {
                new SequenceNode
                {
                    Kind = SequenceNodeKind.Race,
                    Children = new[]
                    {
                        new SequenceNode { Command = "wait", Arguments = new[] { "door_opened" } },
                        new SequenceNode { Command = "delay", Arguments = new[] { "10" } },
                    },
                },
            },
        },
    };
}
```

## Чего раннер не делает {#what-the-runner-does-not-do}

Он не владеет чекпоинтами, состоянием сохранения, допуском, аналитикой, экземплярами ассетов, фокусом UI и
диалогами. Слой туториала добавляет поверх него главы, парсер, валидатор, персистентность и представление;
другие потребители могут подавать узлы напрямую, не завися от него.
