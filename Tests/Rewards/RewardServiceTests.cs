using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foundation.Rewards;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Rewards
{
    public sealed class RewardServiceTests
    {
        [Test]
        public void GrantAsync_DispatchesEachRewardToItsOwnHandler()
        {
            var currency = new RecordingHandler<CurrencyReward>();
            var unlock = new RecordingHandler<UnlockReward>();
            var service = new RewardService();
            service.RegisterHandler(currency);
            service.RegisterHandler(unlock);

            var result = Grant(service, new CurrencyReward("credits", 10), new UnlockReward("moon"));

            Assert.That(result.IsFullyGranted, Is.True);
            Assert.That(currency.Granted, Has.Count.EqualTo(1));
            Assert.That(unlock.Granted, Has.Count.EqualTo(1));
        }

        [Test]
        public void GrantAsync_AppliesRewardsInBundleOrder()
        {
            var order = new List<string>();
            var service = new RewardService();
            service.RegisterHandler(new RecordingHandler<UnlockReward>(order, "unlock"));
            service.RegisterHandler(new RecordingHandler<CurrencyReward>(order, "currency"));

            Grant(service, new UnlockReward("moon"), new CurrencyReward("credits", 5));

            Assert.That(order, Is.EqualTo(new[] { "unlock", "currency" }));
        }

        [Test]
        public void GrantAsync_FailedReward_StopsTheBundleAndReportsSkippedRewards()
        {
            var currency = new RecordingHandler<CurrencyReward>();
            var service = new RewardService();
            service.RegisterHandler(new FailingHandler<UnlockReward>("already-unlocked"));
            service.RegisterHandler(currency);

            var result = Grant(service, new UnlockReward("moon"), new CurrencyReward("credits", 5));

            Assert.That(result.IsFullyGranted, Is.False);
            Assert.That(result.Results.Select(entry => entry.Status),
                Is.EqualTo(new[] { RewardGrantStatus.Failed, RewardGrantStatus.Skipped }));
            Assert.That(result.FirstFailure.FailureCode, Is.EqualTo("already-unlocked"));
            Assert.That(currency.Granted, Is.Empty, "a stopped bundle must not keep granting");
        }

        [Test]
        public void GrantAsync_UnknownRewardType_FailsLoudly()
        {
            var service = new RewardService();

            var exception = Assert.Throws<InvalidOperationException>(
                () => Grant(service, new CurrencyReward("credits", 1)));

            Assert.That(exception.Message, Does.Contain(nameof(CurrencyReward)));
        }

        [Test]
        public void RegisterHandler_Duplicate_IsRejected()
        {
            var service = new RewardService();
            service.RegisterHandler(new RecordingHandler<CurrencyReward>());

            Assert.Throws<InvalidOperationException>(
                () => service.RegisterHandler(new RecordingHandler<CurrencyReward>()));
        }

        [Test]
        public void GrantAsync_EmptyBundle_IsFullyGranted()
        {
            var result = new RewardService().GrantAsync(RewardBundle.Empty).GetAwaiter().GetResult();

            Assert.That(result.IsFullyGranted, Is.True);
            Assert.That(result.Results, Is.Empty);
        }

        [Test]
        public void GrantResult_CarriesHandlerSuppliedDetailsForPresentation()
        {
            var service = new RewardService();
            service.RegisterHandler(new RecordingHandler<CurrencyReward>());

            var result = Grant(service, new CurrencyReward("credits", 42));

            Assert.That(result.Results[0].Details["amount"], Is.EqualTo(42L));
        }

        private static RewardBundleResult Grant(RewardService service, params IReward[] rewards)
            => service.GrantAsync(new RewardBundle(rewards)).GetAwaiter().GetResult();

        // Reward types are declared by the game, never by Foundation. These stand in for them.
        private sealed class CurrencyReward : IReward
        {
            public CurrencyReward(string resourceId, long amount)
            {
                ResourceId = resourceId;
                Amount = amount;
            }

            public string ResourceId { get; }
            public long Amount { get; }
        }

        private sealed class UnlockReward : IReward
        {
            public UnlockReward(string targetId) => TargetId = targetId;

            public string TargetId { get; }
        }

        [Test]
        public void ThrowingHandler_IsReportedAsAFailedGrant_AndTheEarlierRewardsStayReported()
        {
            var service = new RewardService();
            var order = new List<string>();
            service.RegisterHandler(new RecordingHandler<CurrencyReward>(order, "currency"));
            service.RegisterHandler(new ThrowingHandler<UnlockReward>());

            var result = Grant(service, new CurrencyReward("credits", 5), new UnlockReward("moon"), new CurrencyReward("gems", 1));

            Assert.That(result.Results[0].IsGranted, Is.True, "the reward before the exception stays granted and reported");
            Assert.That(result.FirstFailure.FailureCode, Is.EqualTo("handler-failed"));
            Assert.That(result.Results[2].Status, Is.EqualTo(RewardGrantStatus.Skipped));
        }

        private sealed class ThrowingHandler<TReward> : IRewardHandler<TReward> where TReward : IReward
        {
            public UniTask<RewardGrantResult> GrantAsync(TReward reward, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("the store is unreachable");
        }

        private sealed class RecordingHandler<TReward> : IRewardHandler<TReward> where TReward : IReward
        {
            private readonly List<string> _order;
            private readonly string _label;

            public RecordingHandler(List<string> order = null, string label = null)
            {
                _order = order;
                _label = label;
            }

            public List<TReward> Granted { get; } = new();

            public UniTask<RewardGrantResult> GrantAsync(
                TReward reward,
                CancellationToken cancellationToken = default)
            {
                Granted.Add(reward);
                if (_order != null)
                    _order.Add(_label);

                var details = reward is CurrencyReward currency
                    ? new Dictionary<string, object> { ["amount"] = currency.Amount }
                    : null;
                return UniTask.FromResult(RewardGrantResult.Granted(reward, details));
            }
        }

        private sealed class FailingHandler<TReward> : IRewardHandler<TReward> where TReward : IReward
        {
            private readonly string _failureCode;

            public FailingHandler(string failureCode) => _failureCode = failureCode;

            public UniTask<RewardGrantResult> GrantAsync(
                TReward reward,
                CancellationToken cancellationToken = default)
                => UniTask.FromResult(RewardGrantResult.Failed(reward, _failureCode));
        }
    }
}
