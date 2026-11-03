---
title: Реактивные значения и анимация
sidebar_position: 8
---

# Реактивные значения и анимация

Два контракта для того, что меняется во времени. Ни один не требует дополнительной библиотеки: LitMotion —
необязательный адаптер.

## Реактивные значения {#reactive-values}

`Foundation.Reactive` — небольшой API:

| Тип | Роль |
| --- | --- |
| `IEventStream<T>` | `Subscribe(Action<T>)` возвращает `IDisposable`. |
| `IReadOnlyReactiveValue<T>` | Поток событий с текущим `Value`. |
| `EventStream<T>` | Поток событий с методом `Publish`. Исключение одного подписчика записывается в лог и не мешает остальным. |
| `ReactiveValue<T>` | Изменяемое значение. Установка равного значения ничего не публикует. **Подписка сразу доставляет текущее значение**, затем каждое изменение. |

Первый вызов обработчика происходит внутри `Subscribe`: если он выбрасывает исключение, подписка снимается,
а исключение передаётся вызывающему коду. Последующие уведомления идут через `EventStream<T>`, который записывает
ошибки подписчиков в лог. После `ReactiveValue<T>.Dispose()` новая подписка всё ещё один раз получает текущее
значение, но последующих изменений не получает.

Открывайте интерфейс только для чтения, а изменяемый держите приватным:

```csharp compile
using System;
using Foundation.Reactive;

public sealed class Wallet : IDisposable
{
    private readonly ReactiveValue<int> _gold = new(0);
    private readonly EventStream<int> _spent = new();

    public IReadOnlyReactiveValue<int> Gold => _gold;

    public IEventStream<int> Spent => _spent;

    public bool TrySpend(int amount)
    {
        if (amount <= 0 || amount > _gold.Value)
            return false;

        _gold.Value -= amount;
        _spent.Publish(amount);
        return true;
    }

    public void Add(int amount) => _gold.Value += amount;

    public void Dispose()
    {
        _gold.Dispose();
        _spent.Dispose();
    }
}

public sealed class GoldLabel : IDisposable
{
    private readonly IDisposable _subscription;

    public GoldLabel(Wallet wallet)
    {
        // Сразу передаёт текущее значение, затем вызывает обработчик при каждом изменении.
        _subscription = wallet.Gold.Subscribe(OnGoldChanged);
    }

    public void Dispose() => _subscription.Dispose();

    private void OnGoldChanged(int gold)
    {
        // Обновите текст.
    }
}
```

`ILocalizationService.Locale` и потоки кликов и состояния `FoundationButton` используют эти же контракты.

### Владение подписками {#owning-subscriptions}

`DisposableGroup` владеет набором `IDisposable` и освобождает их вместе: например, подписки одного представления или
экрана. `AddTo` передаёт подписку группе прямо там, где она создана.

```csharp compile
using System;
using Foundation.Reactive;

public sealed class WalletView : IDisposable
{
    private readonly DisposableGroup _subscriptions = new();

    public WalletView(Wallet wallet)
    {
        wallet.Gold.Subscribe(OnGoldChanged).AddTo(_subscriptions);
        wallet.Spent.Subscribe(OnSpent).AddTo(_subscriptions);
    }

    public void Dispose() => _subscriptions.Dispose();

    private void OnGoldChanged(int gold) { }

    private void OnSpent(int amount) { }
}
```

- `Clear()` освобождает всё, а группа остаётся пригодной для работы (например, список пересобирается); `Dispose()`
  завершает её, и всё, что добавлено позже, освобождается сразу, поэтому запоздавший колбэк не оставит подписку.
- Элементы освобождаются от новых к старым. Если один бросил исключение, остальные всё равно освобождаются; об ошибке
  сообщается, когда отработали все.
- `Add(Action)` вызывает колбэк при освобождении, `Remove(item)` освобождает один элемент раньше срока. Группа
  предназначена для главного потока.

## Анимация {#motion}

`IMotionService` — контракт, через который компоненты анимируют значения. В Foundation есть `PlayerLoopMotionService` —
небольшой цикл UniTask на `Update` главного цикла Unity, использующий игровое время. Он не хранит состояние отдельных
анимаций, поэтому одного экземпляра достаточно для всей области контейнера. Необязательный адаптер LitMotion
`Foundation.Infrastructure.LitMotion` (`LitMotionService`, `LitMotionInstaller`) доступен при установленном пакете
`com.annulusgames.lit-motion`.

Сервисы не регистрируются автоматически. Зарегистрируйте один `IMotionService` в корневой области. Для LitMotion
вызовите `new LitMotionInstaller().Install(registry)` или добавьте установщик в список установщиков проекта. Компоненты
получают сервис своей области, когда их привязывает инжектор `GameObject`.

Роутер и вкладки пакета не используют анимацию. Стратегии масштаба и цвета кнопок анимируют переходы, только если
`duration` больше нуля и кнопка уже показывает состояние; первое состояние и состояния неактивной кнопки применяются
сразу. Стратегия кнопки или действие обратной связи запрашивает сервис через `RequireMotion()` своего контекста
(`FoundationButtonStrategyContext` или `FeedbackServices`); при отсутствии сервиса возникает исключение.
`MotionEasing.Evaluate(ease, t)` вычисляет кривые для движков, которым это нужно.

`Animate(from, to, duration, ease, state, onUpdate, loop, lifetime)` возвращает `IMotionHandle` со свойством `IsActive` и методами `Cancel()` и `WaitAsync()`. Поддерживаются `float`, `Vector2`, `Vector3`, `Quaternion` и `Color`.
`MotionEase` задаёт кривую интерполяции. `MotionLoop` выбирает однократную анимацию (`Once`), эффект с возвратом
в начальное положение (`PunchOut`) или повторение заданное число раз с возвратом или без него.

Анимация получает объект, который обновляет (`state`), и callback со значением и этим объектом. `static`-callback не
создаёт замыкание в вызывающем коде. Если передан токен времени жизни, адаптер LitMotion также создаёт делегат
освобождения; аллокации внутри самой LitMotion здесь не измерялись. Для обычного `Action<T>` может выделяться память
на замыкающую лямбду. Собственному `IMotionService` достаточно реализовать метод для `float`; остальные типы по
умолчанию используют его.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Motion;
using UnityEngine;

public sealed class PanelSlide
{
    private readonly IMotionService _motion;

    public PanelSlide(IMotionService motion) => _motion = motion;

    public UniTask SlideInAsync(RectTransform panel, CancellationToken cancellationToken)
    {
        var handle = _motion.Animate(
            new Vector2(-600f, 0f),
            Vector2.zero,
            0.25f,
            MotionEase.OutCubic,
            panel,
            static (position, rect) => rect.anchoredPosition = position,
            lifetime: cancellationToken);

        return handle.WaitAsync(cancellationToken);
    }
}
```

Код, который умеет работать без анимации, может получить `IMotionService` через `TryResolve` (см.
[необязательные зависимости](di#optional-dependencies)) и сам задать конечное значение, если сервис не зарегистрирован.
Если код должен использовать API анимации без видимого перехода, зарегистрируйте собственную реализацию
`IMotionService`, которая сразу применяет конечное значение. Встроенным стратегиям кнопок и действиям обратной связи,
вызывающим `RequireMotion()`, зарегистрированный сервис всё равно необходим.
