---
title: Отклики
sidebar_position: 5
---

# Отклики

`Foundation.Feedbacks` воспроизводит короткие звуковые и тактильные отклики на игровые события: например, звук
нажатия и вибрацию или фанфару при получении награды. `FeedbackLibrary` хранит определения `FeedbackDefinition`
под стабильными идентификаторами `FeedbackId`. Вызывать сервис можно из игровой логики, системы наград, обучения,
последовательности действий или интерфейса.

| Тип | Роль |
| --- | --- |
| `FeedbackId` | [Типизированный идентификатор](../systems/typed-ids) определения. |
| `FeedbackDefinition` | ScriptableObject: политика повторов и список шагов, у каждого задержка и `FeedbackAction`. |
| `FeedbackLibrary` | ScriptableObject с определениями. Храните его в контенте проекта. |
| `FeedbackAction` | Переиспользуемое действие в виде `ScriptableObject`. Встроены `PlayAudioFeedback` (`AudioEventId`) и `HapticFeedbackAction` (`HapticFeedbackKind`). |
| `IFeedbackService` | `Play(id, context)` возвращает `FeedbackHandle`; `PlayAsync(id, context, ct)` ожидает его. |
| `FeedbackContext` | Целевой `GameObject` и позиция: `For(component)`, `At(position)`. |
| `ButtonFeedbackBinder` | Сопоставляет `(state, signal)` кнопки `FoundationButton` с идентификатором отклика. |
| `FeedbackPlayer` | Компонент с методом `Play()` для `UnityEvent` и скриптовых последовательностей. |

## Подключение {#wiring}

Зарегистрируйте сервис вместе с библиотекой определений. Он использует `IAudioService`, `IHapticsService` и
`IMotionService`, если они доступны. Для самого сервиса все три зависимости необязательны. Звуковые и тактильные действия пакета без своего сервиса
ничего не делают. Действие, которое анимирует, вызывает `services.RequireMotion()`: если `IMotionService` не
зарегистрирован, оно сразу падает с понятной ошибкой, а не анимирует на скрытом движке по умолчанию.

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Audio;
using Foundation.DI;
using Foundation.Feedbacks;
using UnityEngine;

public sealed class FeedbackWiring
{
    public static IFeatureInstaller Create(FeedbackLibrary library) => new FeedbackInstaller(library);
}

public sealed class RewardPresenter
{
    private readonly IFeedbackService _feedbacks;

    public RewardPresenter(IFeedbackService feedbacks) => _feedbacks = feedbacks;

    public void OnButtonPressed(Component button)
    {
        // Запускает воспроизведение сразу; handle позволяет отменить его или проверить, продолжается ли оно.
        var handle = _feedbacks.Play(FeedbackId.From("ui.click"), FeedbackContext.For(button));
        if (!handle.IsPlaying)
            return;
    }

    public UniTask ShowLegendaryRewardAsync(GameObject reward, CancellationToken cancellationToken)
        => _feedbacks.PlayAsync(FeedbackId.From("reward.legendary"), FeedbackContext.For(reward), cancellationToken);
}

public static class FeedbackAuthoringExample
{
    // Обычно определения создают в виде ассетов; здесь показано содержимое определения "ui.click".
    public static FeedbackDefinition CreateClick()
    {
        return FeedbackDefinition.Create(
            FeedbackId.From("ui.click"),
            new[]
            {
                new FeedbackStep(0f, PlayAudioFeedback.Create(AudioEventId.From("ui.click"))),
            });
    }
}
```

Аудиодействия используют `AudioEventId`. Зарегистрируйте и предварительно загрузите [аудиобиблиотеку](../systems/audio)
до воспроизведения откликов, в которых используются эти действия.

## Правила проигрывания {#playback-rules}

- Определение запускает свои шаги **параллельно**; у каждого шага есть необязательная задержка (в
  нескалированном времени).
- **Повторный запуск.** `Restart` отменяет предыдущее воспроизведение с тем же идентификатором и целью, ждёт
  завершения очистки и запускает его снова. `IgnoreWhilePlaying` возвращает уже выполняющийся экземпляр. Для
  разных целей отклики воспроизводятся независимо.
- **Отмена и уничтожение.** Отмена операции или уничтожение целевого объекта останавливает ожидающие действия.
  Пользовательское действие анимации должно восстанавливать исходные значения transform в блоке `finally`.
  `FeedbackPlayer` отменяет воспроизведение при отключении компонента, поэтому подходит для объектов UI-пула.
- Глобальным звуковым и тактильным действиям цель не нужна. Собственное действие должно проверять
  `FeedbackContext` и само определять поведение при отсутствии цели; сервис не пропускает действие только потому,
  что цель не задана или неактивна. Уничтожение цели по умолчанию отменяет воспроизведение. Установите
  `RequiresTargetLifetime`, если воспроизведение нужно отменять при отключении цели. Если цель уже отключена, отмена
  может не дать действию запуститься.
- Для изменения масштаба, позиции или поворота нужны пользовательские действия либо действия из расширения,
  а также зарегистрированный `IMotionService`. Готовых действий для transform в пакете нет, но сервис анимации
  предусмотрен; см. раздел [Анимация](../core/reactive-motion#motion).

## Собственные действия {#custom-actions}

Собственное действие наследуется от `FeedbackAction`, получает типизированный `FeedbackContext`,
необязательные `FeedbackServices` и токен отмены и возвращает `UniTask`:

```csharp compile
[CreateAssetMenu(menuName = "Game/Feedbacks/Log")]
public sealed class LogFeedbackAction : FeedbackAction
{
    [SerializeField] private string message = "feedback";

    public override UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken)
    {
        Debug.Log(message);
        return UniTask.CompletedTask;
    }
}
```

Для эффектов частиц проект реализует собственное действие с использованием пула объектов.

## Кнопки {#buttons}

`FoundationButton` публикует сигналы `Signals` (Click, PointerDown, PointerUp, Selected, Deselected, LongPress,
Invalid). Кнопка управляет состоянием, локализацией, доступностью и асинхронными обработчиками нажатия, но
**не выбирает звуковой, тактильный или анимационный отклик**. Добавьте `ButtonFeedbackBinder` и сопоставьте пару
(идентификатор состояния, сигнал) с `FeedbackId`. Пустой идентификатор состояния задаёт соответствие по умолчанию.
Биндер работает с любой `FoundationButton` и не меняет API игровой логики.

## Чем это не является {#what-it-is-not}

Отклик — короткая реакция на событие. Переход между экранами управляет анимацией их появления и скрытия, а
[последовательность действий](../systems/sequences) координирует длинные сценарии. Система откликов не заменяет
эти механизмы, редактор временной шкалы, рендерер частиц или отдельные системы звука и вибрации.
