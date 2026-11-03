using System.Threading;
using Cysharp.Threading.Tasks;
using System;

namespace Foundation.FeatureFlags
{
    public interface IFeatureFlagService
    {
        UniTask<bool> IsEnabledAsync(
            string featureId,
            CancellationToken cancellationToken = default);
    }

    public sealed class FeatureFlagService : IFeatureFlagService
    {
        private readonly IFeatureFlagSource _source;

        public FeatureFlagService(IFeatureFlagSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public async UniTask<bool> IsEnabledAsync(
            string featureId,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(featureId))
                throw new ArgumentException("Feature flag Id is required.", nameof(featureId));

            var value = await _source.TryGetAsync(featureId, cancellationToken);
            if (value.HasValue)
                return value.Value;

            throw new InvalidOperationException($"Feature flag '{featureId}' is not configured.");
        }
    }
}
