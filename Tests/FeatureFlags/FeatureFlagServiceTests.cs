using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.FeatureFlags;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.FeatureFlags
{
    public sealed class FeatureFlagServiceTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void IsEnabledAsync_ReturnsTheSourceValue(bool value)
        {
            var source = new RecordingSource(value);
            var service = new FeatureFlagService(source);

            var result = service.IsEnabledAsync("shop").GetAwaiter().GetResult();

            Assert.That(result, Is.EqualTo(value));
            Assert.That(source.RequestedIds, Is.EqualTo(new[] { "shop" }));
        }

        [Test]
        public void IsEnabledAsync_UnknownFlag_ThrowsClearError()
        {
            var service = new FeatureFlagService(new RecordingSource(null));

            var exception = Assert.Throws<InvalidOperationException>(
                () => service.IsEnabledAsync("shop_enabeld").GetAwaiter().GetResult());

            Assert.That(exception.Message, Does.Contain("shop_enabeld"));
        }

        [Test]
        public void IsEnabledAsync_EmptyId_IsRejected()
        {
            var service = new FeatureFlagService(new RecordingSource(true));

            Assert.Throws<ArgumentException>(
                () => service.IsEnabledAsync(" ").GetAwaiter().GetResult());
        }

        private sealed class RecordingSource : IFeatureFlagSource
        {
            private readonly bool? _value;

            public RecordingSource(bool? value) => _value = value;

            public List<string> RequestedIds { get; } = new();

            public UniTask<bool?> TryGetAsync(string featureId, CancellationToken cancellationToken = default)
            {
                RequestedIds.Add(featureId);
                return UniTask.FromResult(_value);
            }
        }
    }
}
