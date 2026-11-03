---
title: Время, планирование и случайность
sidebar_position: 5
---

# Время, планирование и случайность

Три интерфейса позволяют игровому коду не зависеть напрямую от `UnityEngine.Time`, корутин и `System.Random`.
Такую логику проще тестировать.

## Время {#time}

| Контракт | По умолчанию | Примечания |
| --- | --- | --- |
| `ITimeProvider` | `SystemTimeProvider` | `UtcNow`, `Time`, `DeltaTime`, `UnscaledDeltaTime`, `ElapsedSinceBoot` (в Android player это монотонные часы устройства; в остальных случаях, включая Editor, отсчёт ведётся от запуска процесса). |
| `ITimeController` | `UnityTimeController` | `SimulationScale` для часов симуляции, `AnimationScale`, который задаёт `Time.timeScale` Unity. |

Зарегистрируйте эти сервисы в области приложения:

```csharp compile
using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.Randomization;
using Foundation.Scheduling;
using Foundation.Timing;

public sealed class TimeInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<SystemTimeProvider>(ServiceLifetime.Singleton).As<ITimeProvider>();
        registry.Register<UnityTimeController>(ServiceLifetime.Singleton).As<ITimeController>();
        registry.RegisterScheduler();
    }
}
```

`ITimeProvider.UtcNow` возвращает системное время UTC. Пользователь может изменить часы устройства, поэтому
для начисления офлайн-дохода проекту нужна собственная проверка времени.

## Планировщик {#scheduler}

`IScheduler` выполняет одноразовые и повторяющиеся колбэки через один общий планировщик, чтобы каждой системе не
требовался собственный таймер.

```csharp compile
public sealed class AutoSaveTimer : IDisposable
{
    private readonly IScheduledHandle _handle;

    public AutoSaveTimer(IScheduler scheduler)
    {
        _handle = scheduler.ScheduleRepeating(intervalSeconds: 30f, Save);
        scheduler.ScheduleOnce(delaySeconds: 5f, () => UnityEngine.Debug.Log("five seconds in"));
    }

    public void Dispose() => _handle.Dispose();

    private void Save()
    {
    }
}
```

`ScheduleRepeating` по умолчанию использует фиксированную задержку. Полный интервал начинается в тик вызова
колбэка, поэтому подвисание вызовет его один раз, а не серию пропущенных вызовов. Планировщик считает
нескалированное время кадра, поэтому таймеры продолжают идти, когда симуляция замедлена или стоит на паузе.

- Колбэк, бросивший исключение, не останавливает остальные колбэки того же тика. Ошибка пробрасывается после
  того, как они отработают: одно исключение как есть, несколько как `AggregateException`.
- `Dispose` дескриптора отменяет колбэк. `Dispose` самого планировщика отменяет все колбэки.
- Колбэк, запланированный во время тика, начнёт выполняться на следующем тике.
- Вызов `Scheduler.Tick()` из колбэка игнорируется. Время кадра уже продвинуто, и вложенный вызов выполнил бы те
  же таймеры второй раз.
- `RegisterScheduler()` регистрирует общий `Scheduler` в области приложения. [Игровой контекст](../app/game-contexts)
  может зарегистрировать `RegisterScopedScheduler()` в своей области. Такой `IScheduler` использует общий
  планировщик, но отменяет свои задачи при уничтожении области контекста. Таймеры не вызывают уже закрытый контекст.
- При наличии `ITimeIntegrityMonitor` планировщик передаёт ему нескалированное время кадра и время UTC, прошедшее
  с предыдущего тика. В пакете нет готовой реализации этого интерфейса.

### Колбэки с фиксированной частотой {#fixed-rate-callbacks}

Фиксированная задержка не равна фиксированной частоте. Если таймер должен держать частоту, используйте
необязательную возможность `IFixedRateScheduler` через `ScheduleFixedRateRepeating`. Для `IScheduler` она доступна
как метод-расширение:

```csharp compile
public sealed class TickPulse : IDisposable
{
    private readonly IScheduledHandle _handle;

    public TickPulse(IScheduler scheduler)
    {
        _handle = scheduler.ScheduleFixedRateRepeating(
            intervalSeconds: 0.25f,
            callback: Pulse,
            maxCallbacksPerTick: 2,
            backlogPolicy: FixedRateBacklogPolicy.Bounded);
    }

    public void Dispose() => _handle.Dispose();

    private void Pulse()
    {
    }
}
```

Интервал отсчитывается между моментами срабатывания, а не от завершения колбэка. За один тик планировщика
выполняется не больше `maxCallbacksPerTick` колбэков этого таймера; остальные обрабатываются по политике backlog.

| Политика | Что происходит с колбэками, которые не поместились в тик |
| --- | --- |
| `Bounded` (по умолчанию) | Отбрасываются, таймер продолжает с ближайшей границы интервала и сохраняет фазу. |
| `Preserve` | Сохраняются как долг и выполняются в следующих тиках, но не больше лимита за тик. |

`Preserve` может накапливать долг без ограничения, если колбэк не успевает. Если долг становится не конечным или
интервал оказывается ниже точности, нужной для его уменьшения, планировщик деактивирует этот таймер. Так он не
теряет долг молча и не бросает исключение каждый кадр. Тик сообщает `InvalidOperationException` вместе с другими
ошибками колбэков, а остальные таймеры продолжают работу. Для таймера, который может отставать, выбирайте
`Bounded`.

По умолчанию расширение использует `maxCallbacksPerTick: 1` и `FixedRateBacklogPolicy.Bounded`. Если `IScheduler`
не поддерживает `IFixedRateScheduler`, расширение бросает `NotSupportedException`, а не переходит молча на более
медленную фиксированную задержку. `ScopedScheduler` поддерживает обе возможности.

## Случайность {#randomness}

`IRandomService` задаёт единственный интерфейс генерации случайных чисел, от которого зависит игра. В тестах
можно подставить генератор с заданным начальным значением.

| Реализация | Применение |
| --- | --- |
| `SystemRandomService` | Реализация по умолчанию на основе `System.Random`. |
| `DeterministicRandomService` | Алгоритм xoshiro256** с начальным значением, полученным через SplitMix64. Одно и то же значение даёт одинаковую последовательность на разных устройствах. Подходит для симуляции и данных, которые нужно воспроизвести при загрузке или повторе. |

```csharp compile
public sealed class LootRoller
{
    private readonly IRandomService _random;

    public LootRoller(IRandomService random) => _random = random;

    public string RollDrop()
    {
        return _random.NextWeighted(new[]
        {
            new WeightedItem<string>("common", 70),
            new WeightedItem<string>("rare", 25),
            new WeightedItem<string>("legendary", 5),
        });
    }

    public bool RollCrit(int critChancePercent) => _random.RollDice(100).IsAtMost(critChancePercent);

    public void Shuffle(IList<int> deck) => _random.Shuffle(deck);
}
```

`RollDice(sides)` возвращает `DiceRoll` с `Is`, `IsAbove`, `IsAtLeast`, `IsBelow`, `IsAtMost`.

Детерминированный поток случайных чисел составляет только часть воспроизводимой симуляции. Он сам по себе не
делает детерминированными время кадра, ввод или физику; см. [Симуляция](simulation#limits).

### Воспроизводимые потоки {#reproducible-streams}

`DeterministicRandomService` умеет сохранять и восстанавливать свою точную позицию и порождать независимые
потоки:

```csharp compile
public static class DeterministicRandomExample
{
    public static void Run()
    {
        IDeterministicRandom random = new DeterministicRandomService(seed: 42);

        var checkpoint = random.CaptureState();
        var first = random.NextInt(0, 1000);
        random.RestoreState(checkpoint);
        var again = random.NextInt(0, 1000);       // равно первому значению

        // Независимый поток зависит только от начального значения и имени: новые значения из потока loot
        // не меняют последовательность другого игрового модуля.
        var loot = random.Fork("loot");
        UnityEngine.Debug.Log($"{first} {again} {loot.NextInt(0, 10)}");
    }
}
```
