---
title: Туториал
sidebar_position: 4
---

# Туториал

Система обучения на основе глав. У **главы** есть триггер, необязательное требование и упорядоченный список
шагов. `TutorialDirector` слушает триггеры и запускает следующую допустимую главу; прогресс сохраняется
отдельным сегментом [сохранения](../app/save), а прерванная глава продолжается после своего последнего
чекпоинта.

| Тип | Роль |
| --- | --- |
| `TutorialChapterDefinition` | Глава: идентификатор, триггер, шаги, чекпоинты, приоритет, требование. |
| `ITutorialChapterSource` / `TutorialChapterCatalog` | Откуда берутся главы и их упорядоченный набор. |
| `ITutorialStep` | Один шаг. Регистрируется в `TutorialRegistry` по идентификатору. |
| `TutorialService` | Выполняет шаги одной главы по порядку. Главы никогда не идут параллельно. |
| `TutorialDirector` | Сопоставляет триггеры с главами, проверяет требования, запускает следующую допустимую главу. |
| `TutorialProgressState` / `TutorialSaveModule` | Статус каждой главы и его сохранение. |
| `ITutorialTrigger` | Сам сообщает о событии-триггере (например, открытии экрана или достижении порога). |
| `ITutorialPresentationGate` | Подавляет запуск глав, пока экраном владеет что-то другое. |
| `TutorialChapterCompletedRequirement` | `IRequirement`, выполняющийся, как только глава завершена. |

Модуль не зависит от сериализатора: `TutorialConfig` и `TutorialSnapshot` — простые классы, которые
обрабатывает зарегистрированный вами [`ISerializer`](../adapters/serialization).

## Шаги и главы на C# {#steps-and-chapters-in-c}

Самый маленький туториал — класс шага и глава, которая на него ссылается. `TutorialStepId`,
`TutorialChapterId` и `TutorialTriggerId` создаются из строк.

```csharp compile
using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Requirements;
using Foundation.Save;
using Foundation.Tutorial;

public sealed class WelcomeStep : ITutorialStep
{
    public TutorialStepId Id { get; } = new TutorialStepId("welcome.intro");

    public async UniTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        UnityEngine.Debug.Log("Welcome!");
        await UniTask.Delay(TimeSpan.FromSeconds(1), cancellationToken: cancellationToken);
    }
}

public sealed class TipStep : ITutorialStep
{
    public TutorialStepId Id { get; } = new TutorialStepId("welcome.tip");

    public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
    {
        UnityEngine.Debug.Log("Tap the building to collect.");
        return UniTask.CompletedTask;
    }
}

public sealed class FixedChapters : ITutorialChapterSource
{
    public static readonly TutorialTriggerId HomeOpened = new TutorialTriggerId("home-opened");

    public UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(CancellationToken cancellationToken = default)
    {
        var chapter = new TutorialChapterDefinition(
            new TutorialChapterId("welcome"),
            HomeOpened,
            new[] { new TutorialStepId("welcome.intro"), new TutorialStepId("welcome.tip") },
            checkpoints: new[] { new TutorialStepId("welcome.intro") });

        return UniTask.FromResult<IReadOnlyList<TutorialChapterDefinition>>(new[] { chapter });
    }
}
```

**Чекпоинт** — шаг, после которого сохраняется прогресс: возобновлённая глава начинается со следующего шага
после последнего завершённого чекпоинта, а не с самого начала.

В управляемой данными схеме главы берутся из конфигов: `ConfigTutorialChapterSource` читает `TutorialConfig`
(`Id`, `TriggerId`, `Priority`, `StepIds`, `CheckpointStepIds`) через [резолвер конфигов](../app/config) и
принимает `ITutorialChapterRequirements`, чтобы прикрепить требование к каждой главе.

## Подключение {#wiring}

Готового установщика для всей системы нет: части — обычные классы, так что регистрируйте их в собственном установщике
подсистемы. Регистрации через фабрики избегают неоднозначности конструктора у классов с необязательными
параметрами-коллекциями.

```csharp compile
public sealed class TutorialInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<FixedChapters>(ServiceLifetime.Singleton).As<ITutorialChapterSource>();
        registry.Register<TutorialChapterCatalog>(ServiceLifetime.Singleton);
        registry.Register<TutorialProgressState>(ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialRegistry(new ITutorialStep[] { new WelcomeStep(), new TipStep() }),
            ServiceLifetime.Singleton);

        registry.Register<TutorialService>(ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialDirector(
                resolver.Resolve<TutorialChapterCatalog>(),
                resolver.Resolve<TutorialService>(),
                resolver.Resolve<TutorialProgressState>()),
            ServiceLifetime.Singleton);

        registry.Register<TutorialSaveModule>(ServiceLifetime.Singleton).As<IFeatureSaveModule>();
    }
}
```

После загрузки сохранения инициализируйте `TutorialDirector` один раз и возобновите всё прерванное. С этого
момента любой код может поднять триггер:

```csharp compile
public sealed class TutorialBootstrap
{
    private readonly TutorialDirector _director;

    public TutorialBootstrap(TutorialDirector director) => _director = director;

    public async UniTask StartAsync(CancellationToken cancellationToken)
    {
        await _director.InitializeAsync(cancellationToken);
        await _director.ResumeAsync(cancellationToken);
    }

    public void OnHomeScreenOpened() => _director.Raise(FixedChapters.HomeOpened);

    public UniTask OnHomeScreenOpenedAndWaitAsync(CancellationToken cancellationToken)
        => _director.RaiseAndWaitAsync(FixedChapters.HomeOpened, cancellationToken);
}
```

`Raise` — запуск без ожидания завершения. `RaiseAsync` возвращается, когда попытка запуска закончена.
`RaiseAndWaitAsync` ещё и ждёт, пока не останется выполняющихся глав, — для экрана, который должен дождаться
завершения обучения перед продолжением. `WaitForIdleAsync` делает только ожидание.

Триггер означает лишь «оцени заново»: глава, заблокированная требованием или другой выполняющейся главой,
запустится в следующий раз, когда что-то поднимет триггер или вызовет `TryStartNextAsync`.

## Требования {#requirements}

Требование главы — любой [`IRequirement`](../core/requirements-rewards). Глава может зависеть от другой через
`TutorialChapterCompletedRequirement`, который к тому же сочетается с `Requirement.All/Any/Not` и остальной
вашей логикой допуска:

```csharp compile
public sealed class AfterWelcome : ITutorialChapterRequirements
{
    private readonly TutorialProgressState _state;

    public AfterWelcome(TutorialProgressState state) => _state = state;

    public IRequirement GetRequirement(TutorialChapterId chapterId)
        => chapterId.Value == "shop"
            ? new TutorialChapterCompletedRequirement(_state, new TutorialChapterId("welcome"))
            : null;
}
```

## Триггеры и условия показа {#triggers-and-gates}

`ITutorialTrigger` сам поднимает идентификаторы триггеров после того, как `TutorialDirector` подключает его к
приёмнику. Передайте триггеры (и любой `ITutorialPresentationGate`, чей `IsSuppressed` блокирует запуск главы)
в конструктор `TutorialDirector`.

```csharp compile
public sealed class LevelReachedTrigger : ITutorialTrigger
{
    private readonly IEventBus _events;
    private IDisposable _subscription;

    public LevelReachedTrigger(IEventBus events) => _events = events;

    public TutorialTriggerId Id { get; } = new TutorialTriggerId("level-3");

    public void Activate(ITutorialTriggerSink sink)
        => _subscription = _events.Subscribe(new Handler(sink, Id));

    public void Dispose() => _subscription?.Dispose();

    private sealed class Handler : IEventHandler<LevelReachedEvent>
    {
        private readonly ITutorialTriggerSink _sink;
        private readonly TutorialTriggerId _id;

        public Handler(ITutorialTriggerSink sink, TutorialTriggerId id)
        {
            _sink = sink;
            _id = id;
        }

        public UniTask HandleAsync(LevelReachedEvent message, CancellationToken cancellationToken = default)
        {
            if (message.Level >= 3)
                _sink.Raise(_id);

            return UniTask.CompletedTask;
        }
    }
}

public readonly struct LevelReachedEvent
{
    public LevelReachedEvent(int level) => Level = level;

    public int Level { get; }
}
```

## Главы как текст: Flow {#writing-chapters-as-text-flow}

Типовые шаги показа текста, подсветки цели и ожидания нажатия можно описать без отдельного C#-класса для
каждого шага. `Foundation.Tutorial.Flow` компилирует небольшой текстовый DSL в главы и выполняет его на
[скриптовых последовательностях](sequences). Он тоже не зависит от сериализатора.

Сборка `Foundation.Tutorial.Flow` не подключается автоматически: чтобы использовать эти типы, добавьте её в `references`
своего assembly definition.

Один файл на главу:

```text
# comment
chapter tutorial.welcome            # unique chapter id
trigger home-opened                 # trigger id that starts it
priority 0                          # optional; higher runs first when several are ready
checkpoints all                     # optional: all | none (default none)

step tutorial.welcome.intro [checkpoint]
  say tutorial.welcome.hello speaker=tutorial.speaker.guide
  highlight shop.button hand=on
  wait_click shop.button
  finally
    close
end
```

- Идентификаторы шагов — это идентификаторы сохранения и аналитики: никогда не переименовывайте шаги
  выпущенной главы.
- Последний шаг должен оставлять оверлей закрытым на всех путях: ставьте `close` после последующих `say`, `highlight` или
  `show` либо в `finally`, который выполняется после `skip_if`. Валидатор проверяет порядок команд и ранний выход.
- Заключайте в кавычки аргументы с пробелами: `say "some key"`.
- `if [not] <condition> ... [else ...] end` и `race ... end` (каждый прямой потомок `race` — ветвь; первый
  завершившийся побеждает, остальные отменяются).

### Примитивы {#primitives}

| Команда | Значение |
| --- | --- |
| `say <text-key> [continue=tap\|none] [target=<id>] [side=auto\|above\|below\|left\|right] [speaker=<text-key>] [arrow=on\|off]` | Окно рассказчика. `tap` (по умолчанию) ждёт игрока. Если `target` не задан, окно следует за подсветкой. Если подсветки нет, его положение определяет презентер проекта. |
| `highlight <target-id> [padding=<px>] [hand=on\|off] [timeout=<s>]` | Подсветить цель. Ждёт её доступности до `timeout` (по умолчанию 10 с), затем продолжает и пишет предупреждение, если настроен логгер. |
| `clear_highlight` | Убрать подсветку. Фокус также снимается, когда шаг заканчивается. |
| `show` / `hide` | Показать или скрыть оверлей, не закрывая его. |
| `close` | Закрыть оверлей. Следующий `say`, `highlight` или `show` откроет его снова. |
| `wait <wait-key> args...` | Дождаться игрового события (`ITutorialWait`). |
| `wait_until [not] <condition> args...` | Ждать, пока условие не выполнится (опрос каждый кадр). |
| `wait_target <target-id> [timeout=<s>]` | Ждать доступности цели до `timeout` (по умолчанию 10 с). Доступность не гарантирует, что цель видна на экране. |
| `wait_click <target-id> [timeout=<s>]` | Ждать клика по цели. Если цель недоступна в течение `timeout` (по умолчанию 10 с), продолжает и пишет предупреждение при наличии логгера. Ожидание самого клика не ограничено этим временем. |
| `delay <seconds>` | Нескалированная задержка. |
| `action <action-key> args...` | Выполнить игровое действие (`ITutorialAction`). |
| `skip_if [not] <condition> args...` | Завершить шаг сейчас; его `finally` всё равно выполнится. |

Для отображения текста используются ключи [локализации](../app/localization).

### Игровые команды {#game-commands}

`action`, `wait` и условия обращаются к игре через три небольших интерфейса. Класс объявляет свой ключ и
параметры через `[TutorialCommand]`; суффикс `?` помечает необязательный параметр. Валидатор редактора читает
эти объявления, поэтому скрипт, вызывающий неизвестную команду или передающий слишком мало аргументов,
приводит к ошибке сборки конфигурации.

```csharp compile
using Foundation.Tutorial.Flow;

[TutorialCommand("wallet.give", "amount")]
public sealed class GiveCoinsAction : ITutorialAction
{
    private readonly ICommandBus _commands;

    public GiveCoinsAction(ICommandBus commands) => _commands = commands;

    public UniTask ExecuteAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        => _commands.SendAsync(new GiveCoinsCommand(context.IntArgument(0)), cancellationToken);
}

[TutorialCommand("shop.opened")]
public sealed class ShopOpenedWait : ITutorialWait
{
    private readonly IEventBus _events;

    public ShopOpenedWait(IEventBus events) => _events = events;

    public UniTask WaitAsync(TutorialCommandContext context, CancellationToken cancellationToken)
        => _events.WaitForEventAsync<ShopOpenedEvent>(null, cancellationToken);
}

[TutorialCommand("wallet.has_at_least", "amount")]
public sealed class HasCoinsCondition : ITutorialCondition
{
    private readonly ICoinWallet _wallet;

    public HasCoinsCondition(ICoinWallet wallet) => _wallet = wallet;

    public bool Evaluate(TutorialCommandContext context) => _wallet.Coins >= context.IntArgument(0);
}

public interface ICoinWallet
{
    int Coins { get; }
}

public readonly struct GiveCoinsCommand
{
    public GiveCoinsCommand(int amount) => Amount = amount;

    public int Amount { get; }
}

public readonly struct ShopOpenedEvent
{
}
```

В скрипте: `action wallet.give 50`, `wait shop.opened`, `skip_if wallet.has_at_least 500`.

### Цели {#targets}

Цель — стабильный идентификатор на чём-то, на что туториал может указать. Добавьте `TutorialTargetHost` на
корень UI и зарегистрируйте его `Registry` как `ITutorialTargetRegistry`; тогда помечайте объекты интерфейса
компонентом `TutorialTarget` в префабе или из кода:

```csharp compile
using Foundation.UI;
using UnityEngine;

public sealed class ShopButtonView : MonoBehaviour
{
    private void Awake() => TutorialTarget.Attach(gameObject, "shop.button");
}
```

Цель доступна, пока она активна, а `wait_click` / `highlight` находят её по идентификатору, так что на объект,
появившийся позже (всплывающее окно, строка списка), можно указать, как только он появится.

### Представление {#presentation}

Проект предоставляет то, что видит игрок, реализуя три контракта, обычно одним презентером поверх панели
туториала. В Foundation нет ничего про шрифты, анимацию и префабы.

| Контракт | Используется | Назначение |
| --- | --- | --- |
| `ITutorialOverlay` | `show`, `hide`, `close` | Затемняющий слой, блокирующий ввод. `OpenAsync` возвращает false, если не может открыться. |
| `ITutorialDialoguePresenter` | `say` | Показывает `TutorialDialogueRequest` (ключ текста, говорящий, якорь, `TooltipSide`, стрелка) и завершается, когда игрок продолжает. |
| `ITutorialFocusPresenter` | `highlight`, `clear_highlight` | Выделяет область вокруг цели `TutorialFocusRequest`. |

Сторона диалога — это `TooltipSide`, тот же тип, который [`TooltipBubble`](../ui/router#tooltips) использует,
чтобы встать рядом с целью, поэтому один компонент подсказки можно использовать в обоих случаях.

```csharp compile
public sealed class TutorialPanel : ITutorialOverlay, ITutorialDialoguePresenter, ITutorialFocusPresenter
{
    private readonly GameObject _root;

    public TutorialPanel(GameObject root) => _root = root;

    public ITutorialTarget Focused { get; private set; }

    public UniTask<bool> OpenAsync(CancellationToken cancellationToken = default)
    {
        _root.SetActive(true);
        return UniTask.FromResult(true);
    }

    public void SetVisible(bool visible) => _root.SetActive(visible);

    public void Close()
    {
        Focused = null;
        _root.SetActive(false);
    }

    public async UniTask ShowAsync(TutorialDialogueRequest request, CancellationToken cancellationToken = default)
    {
        // Локализуйте request.TextKey и разместите подсказку у request.Anchor со стороны request.Side.
        if (request.WaitForContinue)
            await UniTask.WaitUntil(() => UnityEngine.Input.anyKeyDown, cancellationToken: cancellationToken);
    }

    public void Focus(TutorialFocusRequest request) => Focused = request.Target;

    public void ClearFocus() => Focused = null;
}
```

### Настройка Flow {#putting-flow-together}

Зарегистрируйте три интерфейса представления, реестр целей и классы команд, затем настройте раннер и
используйте `ScriptTutorialChapterSource` как источник глав вместо C#-варианта выше:

```csharp compile
using Foundation.Messaging;

public sealed class FlowTutorialInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<GiveCoinsAction>(ServiceLifetime.Singleton).As<ITutorialAction>();
        registry.Register<ShopOpenedWait>(ServiceLifetime.Singleton).As<ITutorialWait>();
        registry.Register<HasCoinsCondition>(ServiceLifetime.Singleton).As<ITutorialCondition>();

        registry.Register(
            resolver => new TutorialCommandRegistry(
                resolver.Resolve<IReadOnlyList<ITutorialAction>>(),
                resolver.Resolve<IReadOnlyList<ITutorialCondition>>(),
                resolver.Resolve<IReadOnlyList<ITutorialWait>>()),
            ServiceLifetime.Singleton);

        registry.Register(
            resolver => new TutorialScriptRunner(
                resolver.Resolve<ITutorialOverlay>(),
                resolver.Resolve<ITutorialDialoguePresenter>(),
                resolver.Resolve<ITutorialFocusPresenter>(),
                resolver.Resolve<ITutorialTargetRegistry>(),
                resolver.Resolve<TutorialCommandRegistry>()),
            ServiceLifetime.Singleton);

        registry.Register<ScriptTutorialChapterSource>(ServiceLifetime.Singleton).As<ITutorialChapterSource>();
    }
}
```

`ITutorialStepObserver` (передаётся раннеру) получает уведомления о начале и завершении каждого шага — сюда
можно подключить аналитику обучения.

### Сборка конфига {#building-the-config}

Файлы глав компилируются в одну группу конфигов конвейером конфигов редактора (см. [Конфиги](../app/config)):
`TutorialScriptSourceProvider(folder, fileName, localizationCsvPath)` читает каждый `*.txt` в папке (одна
глава на файл), разбирает и проверяет его по типам `[TutorialCommand]` и, если указан путь к CSV, по ключам
локализации; `TutorialScriptExporter` записывает все главы в одну группу `TutorialScriptConfig[]`;
`TutorialScriptConfigValidator` сообщает об ошибках разбора и валидации с файлом и строкой, а также об
идентификаторах глав или шагов, объявленных более чем в одном файле. При любой ошибке сборка завершается
неуспешно.

```csharp
registry.RegisterSourceProvider(new TutorialScriptSourceProvider(
    "Assets/Content/Tutorials", "tutorial_scripts.bytes", localizationSettings.CsvPath));
registry.RegisterExporter(new TutorialScriptExporter());
registry.RegisterValidator(new TutorialScriptConfigValidator());
```

Доставка экспортированного файла (адрес и группа Addressables) — задача проекта.

После трёх последовательных ошибок глава больше не запускается в течение времени жизни экземпляра
`TutorialDirector`, в том числе при последующих триггерах и вызовах `ResumeAsync`. Другие подходящие главы
могут выполняться. Новый экземпляр `TutorialDirector` начинает новый цикл попыток.
