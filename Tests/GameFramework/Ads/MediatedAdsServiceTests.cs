using System.Threading;
using Foundation.GameFramework.Ads;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using System;

namespace Foundation.GameFramework.Tests.Ads
{
    public sealed class MediatedAdsServiceTests
    {
        private sealed class FakeProvider : IAdsProvider
        {
            public string ProviderId { get; }
            public bool Ready;
            public int ShowRewardedCount;
            public int ShowInterstitialCount;

            public FakeProvider(string providerId, bool ready)
            {
                ProviderId = providerId;
                Ready = ready;
            }

            public UniTask<bool> IsReadyAsync(string placementId, CancellationToken cancellationToken = default)
                => UniTask.FromResult(Ready);

            public UniTask<AdShowResult> ShowRewardedAsync(string placementId, CancellationToken cancellationToken = default)
            {
                ShowRewardedCount++;
                return UniTask.FromResult(AdShowResult.Shown);
            }

            public UniTask<AdShowResult> ShowInterstitialAsync(string placementId, CancellationToken cancellationToken = default)
            {
                ShowInterstitialCount++;
                return UniTask.FromResult(AdShowResult.Shown);
            }
        }

        [Test]
        public void ShowRewardedAsync_SkipsNotReadyProviders_UsesFirstReadyOne()
        {
            var notReady = new FakeProvider("a", ready: false);
            var ready = new FakeProvider("b", ready: true);
            var service = new MediatedAdsService(new IAdsProvider[] { notReady, ready });

            var result = service.ShowRewardedAsync("placement").GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(AdShowResult.Shown));
            Assert.That(notReady.ShowRewardedCount, Is.Zero);
            Assert.That(ready.ShowRewardedCount, Is.EqualTo(1));
        }

        [Test]
        public void ShowRewardedAsync_RespectsPriorityOrder_PrefersEarlierReadyProvider()
        {
            var first = new FakeProvider("first", ready: true);
            var second = new FakeProvider("second", ready: true);
            var service = new MediatedAdsService(new IAdsProvider[] { first, second });

            service.ShowRewardedAsync("placement").GetAwaiter().GetResult();

            Assert.That(first.ShowRewardedCount, Is.EqualTo(1));
            Assert.That(second.ShowRewardedCount, Is.Zero);
        }

        [Test]
        public void ShowRewardedAsync_NoProviderReady_ReturnsNoFill()
        {
            var service = new MediatedAdsService(new IAdsProvider[] { new NullAdsProvider() });

            var result = service.ShowRewardedAsync("placement").GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(AdShowResult.NoFill));
        }

        [Test]
        public void IsReadyAsync_TrueWhenAnyProviderIsReady()
        {
            var service = new MediatedAdsService(new IAdsProvider[]
            {
                new FakeProvider("a", ready: false),
                new FakeProvider("b", ready: true)
            });

            Assert.That(service.IsReadyAsync("placement").GetAwaiter().GetResult(), Is.True);
        }

        [Test]
        public void Constructor_EmptyProviderList_Throws()
        {
            Assert.Throws<ArgumentException>(() => new MediatedAdsService(new IAdsProvider[0]));
        }
    }
}
