---
title: Конечный автомат
sidebar_position: 3
---

# Конечный автомат

Универсальный конечный автомат для запуска приложения, навигации между экранами и других последовательностей
с явно заданными состояниями. Состояния создаются из DI-контейнера и получают зависимости через конструктор,
как и остальные сервисы.

| Тип | Роль |
| --- | --- |
| `IState` | Одно состояние: `EnterAsync()`, `Update()`, `ExitAsync()`. |
| `IStateMachine<TState>` / `StateMachine<TState>` | Владеет текущим состоянием. `EnterAsync<TConcrete>()` выходит из текущего состояния, затем входит в следующее. |
| `IStateResolver<TState>` / `DiStateResolver<TState>` | Создаёт следующее состояние. `DiStateResolver` разрешает его из DI-контейнера. |
| `IStateTransitionValidator<TState>` | Необязательная проверка. `AlwaysValidStateTransitionValidator<TState>` разрешает всё. |
| `IStateMachineContext<TState>` | Контракт, через который состояние запрашивает переход: `RequestTransition<TConcrete>()`. |

Если валидатор отклоняет переход, `EnterAsync` завершается без смены состояния.

## Поток запуска {#a-startup-flow}

Сгруппируйте состояния одного автомата под маркерным интерфейсом, чтобы автомат принимал только свои
состояния:

```csharp compile
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.StateMachine;

public interface IAppState : IState
{
}

public sealed class LoadContentState : IAppState
{
    private readonly IStateMachineContext<IAppState> _machine;

    public LoadContentState(IStateMachineContext<IAppState> machine) => _machine = machine;

    public async UniTask EnterAsync()
    {
        await UniTask.Delay(100);             // Загрузите конфигурации, подготовьте сервисы...
        _machine.RequestTransition<MenuState>();
    }

    public void Update()
    {
    }

    public UniTask ExitAsync() => UniTask.CompletedTask;
}

public sealed class MenuState : IAppState
{
    public UniTask EnterAsync() => UniTask.CompletedTask;

    public void Update()
    {
    }

    public UniTask ExitAsync() => UniTask.CompletedTask;
}
```

Вызовы `EnterAsync`, `ExitAsync` и `RequestTransition` проходят через единую очередь FIFO, которой владеет конечный
автомат, поэтому переходы не выполняются одновременно. Запрос из `EnterAsync` состояния ждёт завершения текущего
перехода, включая выход из предыдущего состояния. Это работает и для начального состояния, если оно запущено
напрямую через `IStateMachine.EnterAsync`. Валидатор проверяет каждый переход в момент обработки запроса, исходя из
текущего состояния. При ошибке прямые вызовы `IStateMachine.EnterAsync` и `ExitAsync` возвращают `UniTask` с
исключением. `RequestTransition` не возвращает задачу, поэтому необработанные ошибки таких запросов передаются в
обработчик необработанных исключений UniTask; очередь при этом продолжает выполнять следующие запросы.

Чтобы запросить следующий переход из метода жизненного цикла состояния, используйте
`IStateMachineContext.RequestTransition`. Не ожидайте (`await`) `IStateMachine.EnterAsync` или `ExitAsync` того же
автомата из его `EnterAsync` или `ExitAsync`: эти вызовы встанут в очередь за текущей операцией, которая ждёт их
завершения.

## Подключение {#wiring-it}

Для стандартной регистрации используйте `registry.UseStateMachine<TState>()`, а для собственного валидатора
переходов — `registry.UseStateMachine<TState, TValidator>()`. Ниже показана равнозначная ручная регистрация. В
обоих случаях конкретные состояния нужно зарегистрировать отдельно.

```csharp compile
public sealed class AppStatesInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<DiStateResolver<IAppState>>(ServiceLifetime.Singleton).As<IStateResolver<IAppState>>();
        registry.Register<AlwaysValidStateTransitionValidator<IAppState>>(ServiceLifetime.Singleton)
            .As<IStateTransitionValidator<IAppState>>();
        registry.Register<StateMachine<IAppState>>(ServiceLifetime.Singleton).As<IStateMachine<IAppState>>();
        registry.Register<StateMachineContext<IAppState>>(ServiceLifetime.Singleton).As<IStateMachineContext<IAppState>>();

        registry.Register<LoadContentState>(ServiceLifetime.Singleton);
        registry.Register<MenuState>(ServiceLifetime.Singleton);

        registry.RegisterEntryPoint<AppStateDriver>();
    }
}

public sealed class AppStateDriver : IAsyncStartableEntryPoint, ITickableEntryPoint
{
    private readonly IStateMachine<IAppState> _machine;

    public AppStateDriver(IStateMachine<IAppState> machine) => _machine = machine;

    public UniTask StartAsync(CancellationToken cancellationToken) => _machine.EnterAsync<LoadContentState>();

    public void Tick() => _machine.Update();
}
```

## Правила переходов {#transition-rules}

Реализуйте `IStateTransitionValidator<TState>`, чтобы ограничить граф. `IsValid<TNext>(currentState)` получает
текущее состояние (`null` до первого перехода):

```csharp compile
public sealed class AppTransitionValidator : IStateTransitionValidator<IAppState>
{
    public bool IsValid<TNext>(IAppState currentState) where TNext : IAppState
    {
        // В меню можно перейти только после загрузки контента.
        return typeof(TNext) != typeof(MenuState) || currentState is LoadContentState;
    }
}
```

Зарегистрируйте его вместо `AlwaysValidStateTransitionValidator<IAppState>`.
