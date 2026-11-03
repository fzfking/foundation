using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Logging;
using Cysharp.Threading.Tasks;
using CoreLogger = Foundation.Logging.IAppLogger;

namespace Foundation.GameFramework.Ads
{
    public interface IAdsService : IAdsProvider
    {
    }

    public sealed class MediatedAdsService : IAdsService
    {
        private readonly IReadOnlyList<IAdsProvider> _providersInPriorityOrder;
        private readonly CoreLogger _logger;

        public string ProviderId => "mediated";

        public MediatedAdsService(IReadOnlyList<IAdsProvider> providersInPriorityOrder, CoreLogger logger = null)
        {
            if (providersInPriorityOrder == null || providersInPriorityOrder.Count == 0)
            {
                throw new ArgumentException(
                    "At least one ads provider is required (use NullAdsProvider as an explicit fallback).",
                    nameof(providersInPriorityOrder));
            }

            _providersInPriorityOrder = providersInPriorityOrder;
            _logger = logger;
        }

        public async UniTask<bool> IsReadyAsync(string placementId, CancellationToken cancellationToken = default)
        {
            foreach (var adsProvider in _providersInPriorityOrder)
            {
                if (await adsProvider.IsReadyAsync(placementId, cancellationToken))
                    return true;
            }

            return false;
        }

        public UniTask<AdShowResult> ShowRewardedAsync(string placementId, CancellationToken cancellationToken = default)
            => ShowAsync(placementId, (provider, id, token) => provider.ShowRewardedAsync(id, token), cancellationToken);

        public UniTask<AdShowResult> ShowInterstitialAsync(string placementId, CancellationToken cancellationToken = default)
            => ShowAsync(placementId, (provider, id, token) => provider.ShowInterstitialAsync(id, token), cancellationToken);

        private async UniTask<AdShowResult> ShowAsync(
            string placementId,
            Func<IAdsProvider, string, CancellationToken, UniTask<AdShowResult>> show,
            CancellationToken cancellationToken)
        {
            foreach (var provider in _providersInPriorityOrder)
            {
                if (!await provider.IsReadyAsync(placementId, cancellationToken)) 
                    continue;
                
                // Logged unconditionally, so a log capture always shows which provider served a request:
                // a real network's provider failing silently (mediation whose IsReadyAsync swallows load
                // errors) and a fallback granting the reward instead look the same as an ad that was
                // shown.
                _logger?.Log(LogLevel.Info, "Ads",
                    $"Provider '{provider.ProviderId}' is serving placement '{placementId}'.");
                return await show(provider, placementId, cancellationToken);
            }

            _logger?.Log(LogLevel.Warning, "Ads", $"No provider is ready for placement '{placementId}'.");
            return AdShowResult.NoFill;
        }
    }
}
