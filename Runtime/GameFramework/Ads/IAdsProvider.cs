using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameFramework.Ads
{
    public enum AdShowResult
    {
        /// <summary>A non-rewarded ad (interstitial) played to completion. Rewarded providers
        /// should use <see cref="RewardGranted"/>/<see cref="ClosedWithoutReward"/> instead of this
        /// value, since "shown" alone cannot tell a caller whether a reward was actually earned.</summary>
        Shown,
        Skipped,
        Failed,
        NoFill,

        /// <summary>
        /// Rewarded ad completed and the network confirmed the reward. A provider must resolve its
        /// <see cref="Cysharp.Threading.Tasks.UniTask{T}"/> exactly once with this value even when the SDK fires
        /// several native callbacks for one show (reward, then close).
        /// </summary>
        RewardGranted,

        /// <summary>Rewarded ad was shown (and possibly closed) but no reward was earned - the
        /// player skipped before the reward threshold, or the network reports completion without a
        /// reward. Callers must never treat this as a granted reward.</summary>
        ClosedWithoutReward,
    }

    public interface IAdsProvider
    {
        string ProviderId { get; }
        UniTask<bool> IsReadyAsync(string placementId, CancellationToken cancellationToken = default);
        UniTask<AdShowResult> ShowRewardedAsync(string placementId, CancellationToken cancellationToken = default);
        UniTask<AdShowResult> ShowInterstitialAsync(string placementId, CancellationToken cancellationToken = default);
    }
}
