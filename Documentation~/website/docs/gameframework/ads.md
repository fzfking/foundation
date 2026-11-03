---
title: Ads
sidebar_position: 2
---

# Ads

Gameplay depends on `IAdsService`, not on one ad network. `MediatedAdsService` is a small mediation of your own: it takes
an ordered list of providers and shows the first one that reports ready, falling through the list otherwise.

| Type | Role |
| --- | --- |
| `IAdsProvider` | One network or SDK: `ProviderId`, `IsReadyAsync`, `ShowRewardedAsync`, `ShowInterstitialAsync`. |
| `IAdsService` | What gameplay depends on; the same shape as a provider. |
| `MediatedAdsService` | The root provider that walks an ordered list and shows the first that is ready. |
| `NullAdsProvider` | A no-ads implementation that reports not ready and returns `NoFill` if called directly. The service still requires a valid provider list. |
| `AdShowResult` | `Shown`, `Skipped`, `Failed`, `NoFill`, `RewardGranted`, `ClosedWithoutReward`. |

Concrete network adapters are not part of the package: write one `IAdsProvider` per SDK you integrate.

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
        // Placeholder only: a real adapter returns RewardGranted after the SDK confirms the reward.
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

The grant itself belongs to your rules, not the ad module: when the result is `RewardGranted`, send the command that gives
the reward (see [Requirements and rewards](../core/requirements-rewards)). The service returns the result of the first
ready provider, including `NoFill`. If none is ready, it returns `NoFill` itself. The project decides what to do next.
