---
title: Реклама
sidebar_position: 2
---

# Реклама

Игровой код зависит от `IAdsService`, а не от одной рекламной сети. `MediatedAdsService` — небольшая
простая система медиации: она перебирает провайдеров в заданном порядке и показывает рекламу через первого
готового провайдера.

| Тип | Роль |
| --- | --- |
| `IAdsProvider` | Одна сеть или SDK: `ProviderId`, `IsReadyAsync`, `ShowRewardedAsync`, `ShowInterstitialAsync`. |
| `IAdsService` | От чего зависит игровой код; та же форма, что у провайдера. |
| `MediatedAdsService` | Сервис, который перебирает список по порядку и выбирает первого готового провайдера. |
| `NullAdsProvider` | Реализация без рекламы: она сообщает, что не готова, а при прямом вызове возвращает `NoFill`. Сервису всё равно нужен корректный список провайдеров. |
| `AdShowResult` | `Shown`, `Skipped`, `Failed`, `NoFill`, `RewardGranted`, `ClosedWithoutReward`. |

Конкретные адаптеры сетей в пакет не входят: пишите по одному `IAdsProvider` на каждый интегрируемый SDK.

```csharp compile
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.GameFramework.Ads;

public sealed class MyNetworkAds : IAdsProvider
{
    public string ProviderId => "my-network";

    public UniTask<bool> IsReadyAsync(string placementId, CancellationToken cancellationToken = default)
        => UniTask.FromResult(true);

    public UniTask<AdShowResult> ShowRewardedAsync(string placementId, CancellationToken cancellationToken = default)
    {
        // Это только заглушка: реальный адаптер возвращает RewardGranted после подтверждения награды SDK.
        return UniTask.FromResult(AdShowResult.NoFill);
    }

    public UniTask<AdShowResult> ShowInterstitialAsync(string placementId, CancellationToken cancellationToken = default)
        => UniTask.FromResult(AdShowResult.NoFill);
}

public sealed class AdsInstaller : IFeatureInstaller
{
    public void Install(IServiceRegistry registry)
    {
        registry.Register(
            _ => new MediatedAdsService(new IAdsProvider[] { new MyNetworkAds(), new NullAdsProvider() }),
            ServiceLifetime.Singleton).As<IAdsService>();
    }
}

public sealed class DoubleRewardOffer
{
    private readonly IAdsService _ads;

    public DoubleRewardOffer(IAdsService ads) => _ads = ads;

    public async UniTask<bool> TryDoubleAsync(CancellationToken cancellationToken)
    {
        var result = await _ads.ShowRewardedAsync("end_of_level_bonus", cancellationToken);
        return result == AdShowResult.RewardGranted;
    }
}
```

Награду выдаёт игровая логика, а не рекламный модуль: когда результат `RewardGranted`,
отправьте команду, которая выдаёт награду (см. [Требования и награды](../core/requirements-rewards)). Сервис
возвращает результат первого готового провайдера, в том числе `NoFill`. Если готовых провайдеров нет, сервис сам
возвращает `NoFill`. Проект решает, что показать или выдать игроку при отсутствии рекламы.
