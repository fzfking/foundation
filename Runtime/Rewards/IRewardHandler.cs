using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Rewards
{
    public interface IRewardHandler<in TReward> where TReward : IReward
    {
        UniTask<RewardGrantResult> GrantAsync(TReward reward, CancellationToken cancellationToken = default);
    }
}
