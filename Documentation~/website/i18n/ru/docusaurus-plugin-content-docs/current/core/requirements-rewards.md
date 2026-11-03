---
title: Требования и награды
sidebar_position: 6
---

# Требования и награды

Два механизма игровой логики: проверка условий действия и выдача наград.

## Требования {#requirements}

Требование проверяет, может ли игрок выполнить действие сейчас. Если нет, оно возвращает стабильный код
причины и аргументы, которые UI может передать в локализацию. Оно служит для допуска и диагностики и
не заменяет проверку внутри транзакции команды, которая действительно изменяет состояние.

| Тип | Роль |
| --- | --- |
| `ICondition` | Единичный предикат: `IsSatisfied()`. |
| `IRequirement` | `Evaluate()` возвращает `RequirementResult`. |
| `RequirementResult` | `IsSatisfied`, а при невыполнении — `Failures` (у каждого код и аргументы), `FailureCode`, `FailureArguments`, `HasFailure(code)`. |
| `Requirement` | Фабрики: `FromCondition`, `FromPredicate`, `All`, `Any`, `Not`; `Always` — свойство. |

`All` собирает отказы всех дочерних требований. `Any` считается выполненным, если выполнено хотя бы одно дочернее
требование; иначе он возвращает причины отказа всех дочерних требований. `Not` инвертирует требование и использует
собственный код отказа.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Requirements;
using Foundation.Rewards;

public sealed class PlayerProgress
{
    public int Level { get; set; }

    public bool HasTutorialDone { get; set; }
}

public static class UnlockRules
{
    public static IRequirement ForShop(PlayerProgress progress)
    {
        return Requirement.All(
            Requirement.FromPredicate(
                () => progress.Level >= 5,
                "level_too_low",
                new Dictionary<string, object> { ["level"] = 5 }),
            Requirement.FromPredicate(() => progress.HasTutorialDone, "tutorial_not_done"));
    }
}

public static class UnlockRulesExample
{
    public static string Describe(PlayerProgress progress)
    {
        var result = UnlockRules.ForShop(progress).Evaluate();
        if (result.IsSatisfied)
            return "open";

        // FailureCode содержит первую причину отказа, а Failures — все причины.
        return result.HasFailure("level_too_low")
            ? $"Reach level {result.FailureArguments["level"]} to unlock"
            : result.FailureCode;
    }
}
```

Код отказа и аргументы — это данные, а не текст: сопоставляйте их с [ключом локализации](../app/localization)
на уровне представления.

Требование может быть и долгоживущим объектом, например заблокированная вкладка, которая хранит своё
требование и пересчитывает его при изменении состояния игрока. Главы туториала используют тот же контракт
(`TutorialChapterCompletedRequirement`).

## Награды {#rewards}

`IRewardService.GrantAsync(RewardBundle)` передаёт каждую награду обработчику, зарегистрированному на её
точный тип. Награда не обязана быть предметом: валюта, разблокировки и всё, что придумает проект, допустимы, а
обработчик решает, что в этом проекте означает «выдать».

| Тип | Роль |
| --- | --- |
| `IReward` | Маркер типа награды. |
| `IRewardHandler<TReward>` | `GrantAsync(reward, ct)` возвращает `RewardGrantResult`. |
| `RewardService` | `RegisterHandler<TReward>(handler)` (один на тип) и `GrantAsync`. |
| `RewardBundle` | Упорядоченный список наград. |
| `RewardGrantResult` | `Granted(reward)`, `Failed(reward, code, details)` или `Skipped(reward)`. |
| `RewardBundleResult` | `Results`, `IsFullyGranted`, `FirstFailure`. |

```csharp compile
public sealed class CurrencyReward : IReward
{
    public CurrencyReward(string currencyId, int amount)
    {
        CurrencyId = currencyId;
        Amount = amount;
    }

    public string CurrencyId { get; }

    public int Amount { get; }
}

public sealed class CurrencyRewardHandler : IRewardHandler<CurrencyReward>
{
    private readonly Dictionary<string, int> _balances = new();

    public UniTask<RewardGrantResult> GrantAsync(CurrencyReward reward, CancellationToken cancellationToken = default)
    {
        if (reward.Amount <= 0)
            return UniTask.FromResult(RewardGrantResult.Failed(reward, "invalid_amount"));

        _balances.TryGetValue(reward.CurrencyId, out var current);
        _balances[reward.CurrencyId] = current + reward.Amount;
        return UniTask.FromResult(RewardGrantResult.Granted(reward));
    }
}

public sealed class RewardsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register<CurrencyRewardHandler>(ServiceLifetime.Singleton);
        registry.Register<RewardService>(ServiceLifetime.Singleton).As<IRewardService>().AsSelf();
        registry.RegisterBuildCallback(resolver =>
            resolver.Resolve<RewardService>().RegisterHandler(resolver.Resolve<CurrencyRewardHandler>()));
    }
}

public sealed class DailyGift
{
    private readonly IRewardService _rewards;

    public DailyGift(IRewardService rewards) => _rewards = rewards;

    public async UniTask<bool> ClaimAsync(CancellationToken cancellationToken)
    {
        var result = await _rewards.GrantAsync(
            new RewardBundle(new CurrencyReward("gold", 100), new CurrencyReward("gems", 5)),
            cancellationToken);

        return result.IsFullyGranted;
    }
}
```

### Что гарантирует набор {#what-a-bundle-guarantees}

- Награды выдаются **по порядку**, по одной.
- Сервис **останавливается после первой не выданной награды**; остальные получают результат `Skipped`.
- Набор **не атомарен**: награды, выданные до сбоя, остаются выданными. Не предполагайте атомарность, если её
  не обеспечивают ваши обработчики.
- Обычное исключение обработчика даёт результат «не выдано» с кодом `handler-failed`; текст исключения попадает в
  `Details["exception"]`. Ранее выданные награды остаются выданными и попадают в результат. Если переданный токен отменён и
  обработчик выбрасывает `OperationCanceledException`, отмена передаётся вызывающему коду, а `GrantAsync` не возвращает
  результат набора наград.
- Тип награды без обработчика бросает `InvalidOperationException`; второй обработчик для одного типа
  отклоняется.
- Обработчики сопоставляются с точным типом награды во время выполнения, а не с базовым типом.
