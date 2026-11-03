using System.Collections.Generic;
using System.Threading;
using Foundation.Analytics;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests
{
    public sealed class AnalyticsServiceTests
    {
        [Test]
        public void AnalyticsEvent_IsForwardedThroughEventBus()
        {
            var provider = new TestProvider();
            var bus = new EventBus();
            var service = new AnalyticsService(bus, provider);
            service.Start();
            bus.PublishAsync(new TestAnalyticsEvent("planet_unlocked"))
                .GetAwaiter().GetResult();

            Assert.That(provider.LastEventName, Is.EqualTo("planet_unlocked"));
            service.Dispose();
        }

        private sealed class TestProvider : IAnalyticsProvider
        {
            public string LastEventName { get; private set; }

            public UniTask TrackAsync(
                IAnalyticsEvent analyticsEvent,
                CancellationToken cancellationToken = default)
            {
                LastEventName = analyticsEvent.Name;
                return UniTask.CompletedTask;
            }
        }

        private sealed class TestAnalyticsEvent : IAnalyticsEvent
        {
            public TestAnalyticsEvent(string name)
            {
                Name = name;
            }

            public string Name { get; }

            public IReadOnlyDictionary<string, object> Parameters =>
                new Dictionary<string, object>();
        }
    }
}
