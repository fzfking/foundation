using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Ads
{
    public sealed class NullAdsProvider : IAdsProvider
    {
        public string ProviderId => "null";

        public UniTask<bool> IsReadyAsync(string placementId, CancellationToken cancellationToken = default)
            => UniTask.FromResult(false);

        public UniTask<AdShowResult> ShowRewardedAsync(string placementId, CancellationToken cancellationToken = default)
            => UniTask.FromResult(AdShowResult.NoFill);

        public UniTask<AdShowResult> ShowInterstitialAsync(string placementId, CancellationToken cancellationToken = default)
            => UniTask.FromResult(AdShowResult.NoFill);
    }
}
