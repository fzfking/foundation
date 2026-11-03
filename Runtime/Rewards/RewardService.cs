using System.Threading;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Foundation.Rewards
{
    public interface IRewardService
    {
        UniTask<RewardBundleResult> GrantAsync(RewardBundle bundle, CancellationToken cancellationToken = default);
    }

    public sealed class RewardService : IRewardService
    {
        private readonly Dictionary<Type, Func<IReward, CancellationToken, UniTask<RewardGrantResult>>> _handlers = new();

        public void RegisterHandler<TReward>(IRewardHandler<TReward> handler) where TReward : IReward
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (_handlers.ContainsKey(typeof(TReward)))
                throw new InvalidOperationException(
                    $"A reward handler for '{typeof(TReward).FullName}' is already registered.");

            _handlers.Add(
                typeof(TReward),
                (reward, cancellationToken) => handler.GrantAsync((TReward)reward, cancellationToken));
        }

        public async UniTask<RewardBundleResult> GrantAsync(
            RewardBundle bundle,
            CancellationToken cancellationToken = default)
        {
            if (bundle == null)
                throw new ArgumentNullException(nameof(bundle));

            var results = new List<RewardGrantResult>(bundle.Count);
            var stopped = false;
            foreach (var reward in bundle)
            {
                if (stopped)
                {
                    results.Add(RewardGrantResult.Skipped(reward));
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var result = await InvokeHandlerAsync(ResolveHandler(reward), reward, cancellationToken);
                results.Add(result);
                stopped = !result.IsGranted;
            }

            return new RewardBundleResult(results);
        }

        // A handler that throws is a failed grant, not a lost bundle: the rewards before it are still reported as granted,
        // so the caller never has to guess what was handed out before the exception.
        private static async UniTask<RewardGrantResult> InvokeHandlerAsync(
            Func<IReward, CancellationToken, UniTask<RewardGrantResult>> handler,
            IReward reward,
            CancellationToken cancellationToken)
        {
            RewardGrantResult result;
            try
            {
                result = await handler(reward, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RewardGrantResult.Failed(
                    reward,
                    "handler-failed",
                    new Dictionary<string, object> { ["exception"] = exception.Message });
            }

            return result ?? throw new InvalidOperationException(
                $"Reward handler for '{reward.GetType().FullName}' returned no result.");
        }

        private Func<IReward, CancellationToken, UniTask<RewardGrantResult>> ResolveHandler(IReward reward)
        {
            if (_handlers.TryGetValue(reward.GetType(), out var handler))
                return handler;

            throw new InvalidOperationException(
                $"No reward handler is registered for '{reward.GetType().FullName}'.");
        }
    }
}
