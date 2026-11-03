---
title: Компоненты uGUI
sidebar_position: 2
---

# Компоненты uGUI

`Foundation.UI.UGui` содержит переиспользуемые компоненты представления на uGUI. Это отдельная сборка, которая
использует собственные реактивные типы Foundation и не зависит от стороннего рендерера текста, DI-контейнера,
реактивной библиотеки или библиотеки анимаций.

## FoundationButton {#foundationbutton}

Готовая замена `UnityEngine.UI.Button` с:

- задаваемыми в инспекторе строковыми **состояниями** (`normal`, `locked`, `busy`, `selected` или
  идентификаторами проекта);
- ограничением ввода для каждого состояния, ключом локализации и аргументами, стратегиями состояния, а также
  `UnityEvent` входа и выхода для каждого состояния;
- потоком событий `Clicked` и реактивными значениями `State` и `Busy` ([Реактивные
  значения](../core/reactive-motion));
- `SubscribeAsync`, получающим токен отмены и **удерживающим кнопку заблокированной, пока не завершатся все
  зарегистрированные асинхронные обработчики**;
- **стратегиями** на каждое состояние, которые можно комбинировать и создавать как ScriptableObject или
  компоненты сцены;
- семантическими `Signals` для ввода, выбора, долгого нажатия и неудачных попыток клика.

Подпишитесь на клики из кода, для конкретного состояния или для всех:

```csharp compile
using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using Foundation.UI.UGui;
using UnityEngine;

public readonly struct StartRunCommand
{
}

public sealed class PlayButtonPresenter : IDisposable
{
    private readonly IDisposable _click;

    public PlayButtonPresenter(FoundationButton button, ICommandBus commands)
    {
        // Кнопка заблокирована до завершения команды, поэтому двойное нажатие не отправит её дважды.
        _click = button.SubscribeAsync(
            "normal",
            (click, cancellationToken) => commands.SendAsync(new StartRunCommand(), cancellationToken));
    }

    public void Dispose() => _click.Dispose();
}

public static class ButtonStateExample
{
    public static void Lock(FoundationButton button)
    {
        if (button.HasState("locked"))
            button.SetState("locked");
    }
}
```

Используйте `Clicked` (или `Subscribe(stateId, handler)`) для наблюдателей, которые не отвечают за завершение.
Кнопка никогда не предполагает, что произвольный подписчик асинхронный, поэтому клиенту, которому нужна
блокировка, надо использовать `SubscribeAsync`.

### Стратегии состояния {#state-strategies}

Стратегия реагирует на смену состояния. Создавайте переиспользуемые ассеты стратегий через **Foundation → UI →
Button State Strategies** и комбинируйте их в состоянии. Встроены: смена цвета, масштаб, загрузка спрайта
через `IAssetProvider` и переключение активных дочерних вариантов. Стратегия получает необходимые сервисы во время выполнения через
`FoundationButtonStrategyContext`; сервисы передаются ей без ручной настройки со стороны вызывающего кода.

Стратегии цвета и масштаба анимируют изменение за `duration` (0 применяет сразу) и не задерживают следующие за ними
стратегии. Масштаб — множитель к собственному размеру кнопки, оформленный как `ScaleContribution`: нажатие, выбранная
вкладка и любой другой эффект на том же объекте перемножаются и сами себя снимают. Перекрывающиеся эффекты возвращают
исходный размер, если другой код не присваивает `Transform.localScale` напрямую, пока активен вклад. Для всех
изменений масштаба этого объекта используйте `ScaleContribution`. Первое состояние кнопки, а также состояние,
применённое до привязки её скоупа или при
неактивной кнопке, выставляется сразу; следующие изменения анимируются через `IMotionService` скоупа
([Реактивные значения и анимация](../core/reactive-motion#motion)).

Список стратегий использует `InterfaceReference<T>` — сериализуемую ссылку на `UnityEngine.Object`,
реализующий интерфейс стратегии. Выбор принимает ассет ScriptableObject или компонент `MonoBehaviour` на сцене
(перетаскивание GameObject выбирает подходящий компонент). Поэтому стратегия, созданная на сцене, может
нацеливаться на конкретный `Image` без поиска по имени в иерархии.

Инспектор — редактор на IMGUI; проект, предпочитающий другой, может подключить его через
`FoundationButtonInspectorPresentation.Factory`.

Короткие звуковые, тактильные и анимационные отклики подключаются через [отклики](feedbacks) с помощью
`ButtonFeedbackBinder`.

## LocalizedText {#localizedtext}

`LocalizedText` подписывается на `ILocalizationService.Locale`, перерисовывается при смене локали,
поддерживает смену ключа во время работы и передаёт сериализованные или переданные во время выполнения аргументы в
`ILocalizationFormatter` проекта. Отрисованный текст идёт через `ILocalizedTextTarget` — один метод, который
ваш проект реализует для своего рендерера текста, так что `Foundation` остаётся независимым от любого
рендерера. Подробности и пример реализации: [Локализация](../app/localization#text-on-screen-localizedtext).

## Роутер, вкладки и всплывающие подсказки {#router-tabs-and-tooltips}

- `UGuiViewFactory`, `TransformUIRoot` / `LayerUIRoot` и `UIRouterFactory` — uGUI-реализация [UI-роутера](router).
- `UITabGroup`, `UITabItem` и `UITabSelectionVisual` реализуют [вкладки](router#tabs).
- `TooltipBubble` реализует [всплывающие подсказки](router#tooltips).
