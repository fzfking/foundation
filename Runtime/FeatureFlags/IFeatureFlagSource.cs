using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.FeatureFlags
{
    public interface IFeatureFlagSource
    {
        UniTask<bool?> TryGetAsync(string featureId, CancellationToken cancellationToken = default);
    }
}
