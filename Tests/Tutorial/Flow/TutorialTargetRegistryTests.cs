using Foundation.Tutorial.Flow;
using NUnit.Framework;

namespace Foundation.Tests.Tutorial.Flow
{
    public sealed class TutorialTargetRegistryTests
    {
        [Test]
        public void Registry_ReturnsLatestAvailableTarget_AndFallsBackWhenItGoesAway()
        {
            var registry = new TutorialTargetRegistry();
            var first = new FakeTarget("prod.buy");
            var second = new FakeTarget("prod.buy");
            registry.Register(first);
            registry.Register(second);

            Assert.That(registry.TryGet("prod.buy", out var found) ? found : null, Is.SameAs(second));

            second.IsAvailable = false;
            Assert.That(registry.TryGet("prod.buy", out found) ? found : null, Is.SameAs(first));

            registry.Unregister(first, "prod.buy");
            Assert.That(registry.TryGet("prod.buy", out _), Is.False);

            second.IsAvailable = true;
            Assert.That(registry.TryGet("prod.buy", out found) ? found : null, Is.SameAs(second));
        }
    }
}
