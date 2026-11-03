using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Foundation.Badges.Tests
{
    public sealed class BadgeServiceTests
    {
        private sealed class Source : IAcknowledgeableBadgeSource
        {
            public Source(string id, BadgeValue value)
            {
                Id = BadgeId.From(id);
                Value = value;
            }

            public BadgeId Id { get; }
            public BadgeValue Value { get; private set; }
            public event Action Changed;

            public void Set(BadgeValue value)
            {
                Value = value;
                Changed?.Invoke();
            }

            public void Acknowledge() => Set(BadgeValue.None);
        }

        [Test]
        public void SourceUpdatesAndAcknowledgeChangeObservedValue()
        {
            using var badges = new BadgeService(new[] { new BadgeDefinition(BadgeId.From("news")) });
            var source = new Source("news", BadgeValue.New);
            var values = new List<BadgeValue>();
            badges.RegisterSource(source);
            using var subscription = badges.Observe(source.Id).Subscribe(values.Add);

            source.Set(BadgeValue.Dot);
            source.Set(BadgeValue.FromCount(3));
            source.Set(BadgeValue.FromCount(0));
            source.Set(BadgeValue.New);
            Assert.That(badges.Acknowledge(source.Id), Is.True);

            Assert.That(values, Is.EqualTo(new[]
            {
                BadgeValue.New, BadgeValue.Dot, BadgeValue.FromCount(3), BadgeValue.None,
                BadgeValue.New, BadgeValue.None,
            }));
            source.Set(BadgeValue.Dot);
            badges.UnregisterSource(source);
            Assert.That(badges.Get(source.Id), Is.EqualTo(BadgeValue.None));
        }

        [TestCase(BadgeAggregation.Any, BadgeKind.Dot, 0)]
        [TestCase(BadgeAggregation.Sum, BadgeKind.Count, 11)]
        [TestCase(BadgeAggregation.Max, BadgeKind.Count, 7)]
        public void AggregationPropagatesThroughRoot(BadgeAggregation policy, BadgeKind expectedKind, int expectedCount)
        {
            using var badges = new BadgeService(new[]
            {
                new BadgeDefinition(BadgeId.From("root"), aggregation: BadgeAggregation.Any),
                new BadgeDefinition(BadgeId.From("main"), BadgeId.From("root"), policy),
                new BadgeDefinition(BadgeId.From("managers"), BadgeId.From("main")),
                new BadgeDefinition(BadgeId.From("upgrades"), BadgeId.From("main")),
                new BadgeDefinition(BadgeId.From("events"), BadgeId.From("main")),
            });
            var managers = new Source("managers", BadgeValue.FromCount(3));
            var upgrades = new Source("upgrades", BadgeValue.FromCount(7));
            var events = new Source("events", BadgeValue.FromCount(1));
            badges.RegisterSource(managers);
            badges.RegisterSource(upgrades);
            badges.RegisterSource(events);

            var main = badges.Get(BadgeId.From("main"));
            Assert.That(main.Kind, Is.EqualTo(expectedKind));
            Assert.That(main.Count, Is.EqualTo(expectedCount));
            Assert.That(badges.Get(BadgeId.From("root")), Is.EqualTo(BadgeValue.Dot));
        }

        [Test]
        public void UnrelatedBranchDoesNotPublishAndUnsubscriptionStopsDelivery()
        {
            using var badges = new BadgeService(new[]
            {
                new BadgeDefinition(BadgeId.From("left")),
                new BadgeDefinition(BadgeId.From("right")),
            });
            var left = new Source("left", BadgeValue.None);
            var right = new Source("right", BadgeValue.None);
            badges.RegisterSource(left);
            badges.RegisterSource(right);
            var values = new List<BadgeValue>();
            var subscription = badges.Observe(right.Id).Subscribe(values.Add);
            left.Set(BadgeValue.Dot);
            Assert.That(values, Is.EqualTo(new[] { BadgeValue.None }));
            right.Set(BadgeValue.New);
            subscription.Dispose();
            right.Set(BadgeValue.Dot);
            Assert.That(values, Is.EqualTo(new[] { BadgeValue.None, BadgeValue.New }));
        }

        [Test]
        public void GraphRejectsCyclesAndInvalidIds()
        {
            Assert.Throws<ArgumentException>(() => new BadgeService(new[]
            {
                new BadgeDefinition(BadgeId.From("a"), BadgeId.From("b")),
                new BadgeDefinition(BadgeId.From("b"), BadgeId.From("a")),
            }));
            Assert.Throws<ArgumentException>(() => new BadgeService(new[]
            {
                new BadgeDefinition(BadgeId.From("a")),
                new BadgeDefinition(BadgeId.From("a")),
            }));
            Assert.Throws<ArgumentException>(() => new BadgeService(new[]
            {
                new BadgeDefinition(BadgeId.From("a"), BadgeId.From("missing")),
            }));
            using var badges = new BadgeService(new[] { new BadgeDefinition(BadgeId.From("a")) });
            Assert.Throws<ArgumentException>(() => badges.Get(default));
            Assert.Throws<ArgumentException>(() => badges.Get(BadgeId.From("missing")));
            Assert.That(badges.Acknowledge(BadgeId.From("a")), Is.False);
        }
    }
}
