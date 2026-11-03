---
title: Requirements and rewards
sidebar_position: 6
---

# Requirements and rewards

Two small pipelines for gameplay gating and granting.

## Requirements

A requirement answers "can the player do this right now", and when not, **why**, with a stable failure code and
arguments that a UI can localize. It is for eligibility and diagnostics: it does not replace the transaction check
inside the command that actually mutates state.

| Type | Role |
| --- | --- |
| `ICondition` | A single predicate: `IsSatisfied()`. |
| `IRequirement` | `Evaluate()` returns a `RequirementResult`. |
| `RequirementResult` | `IsSatisfied`, and when not: `Failures` (code and arguments each), `FailureCode`, `FailureArguments`, `HasFailure(code)`. |
| `Requirement` | Factories: `FromCondition`, `FromPredicate`, `All`, `Any`, `Not`; `Always` is a property. |

`All` collects the failures of every child, `Any` fails only when all children fail, `Not` inverts a requirement with
its own failure code.

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

        // FailureCode is the first failure; Failures lists all of them.
        return result.HasFailure("level_too_low")
            ? $"Reach level {result.FailureArguments["level"]} to unlock"
            : result.FailureCode;
    }
}
```

The failure code and arguments are data, not text: map them to a [localization key](../app/localization) in the
presentation layer.

A requirement can also be a long-lived object, for example a locked tab holding its requirement and re-evaluating it when
the player's state changes. The tutorial chapters use the same contract (`TutorialChapterCompletedRequirement`).

## Rewards

`IRewardService.GrantAsync(RewardBundle)` hands each reward to the handler registered for its exact type. A reward does
not have to be an item: currency, unlocks and anything else a project invents are valid, and a handler decides what
"granting" does in that project.

| Type | Role |
| --- | --- |
| `IReward` | Marker for a reward type. |
| `IRewardHandler<TReward>` | `GrantAsync(reward, ct)` returns a `RewardGrantResult`. |
| `RewardService` | `RegisterHandler<TReward>(handler)` (one per type) and `GrantAsync`. |
| `RewardBundle` | An ordered list of rewards. |
| `RewardGrantResult` | `Granted(reward)`, `Failed(reward, code, details)` or `Skipped(reward)`. |
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

### What a bundle guarantees

- Rewards are granted **in order**, one at a time.
- The service **stops after the first reward that is not granted**; the rest are reported as `Skipped`.
- A bundle is **not atomic**: rewards granted before the failure stay granted. Do not assume atomicity unless your
  handlers provide it.
- An ordinary exception from a handler is reported as a failed grant with the code `handler-failed`; its message is in
  `Details["exception"]`. Rewards granted before it remain granted and appear in the result. If the supplied token is canceled and the
  handler throws `OperationCanceledException`, cancellation propagates and `GrantAsync` does not return a bundle result.
- A reward type without a handler throws `InvalidOperationException`; a second handler for one type is rejected.
- Handlers are matched by the reward's exact runtime type, not by base type.
