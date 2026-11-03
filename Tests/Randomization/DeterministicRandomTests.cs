using System;
using Foundation.Randomization;
using NUnit.Framework;

namespace Foundation.Tests.Randomization
{
    // A run resumed from a save, or a replay, must draw the same values on every device: the sequence is fixed by
    // the seed, survives a save, and one system's draws never shift another's stream.
    public sealed class DeterministicRandomTests
    {
        // xoshiro256** seeded through SplitMix64 from 42, computed with an independent implementation of the published
        // algorithms. A change here breaks every saved run and replay.
        [Test]
        public void TheSequenceOfASeed_IsTheReferenceSequence()
        {
            var random = new DeterministicRandomService(42);

            Assert.That(random.NextUInt64(), Is.EqualTo(0x15780b2e0c2ec716UL));
            Assert.That(random.NextUInt64(), Is.EqualTo(0x6104d9866d113a7eUL));
            Assert.That(random.NextUInt64(), Is.EqualTo(0xae17533239e499a1UL));
        }

        [Test]
        public void ARestoredState_ContinuesExactlyWhereTheCapturedSourceWas()
        {
            var original = new DeterministicRandomService(7);
            for (var i = 0; i < 10; i++)
                original.NextInt(0, 100);
            var saved = original.CaptureState();
            var expected = new[] { original.NextInt(0, 1000), original.NextInt(0, 1000), original.NextInt(0, 1000) };

            var resumed = new DeterministicRandomService(7);
            resumed.RestoreState(saved);

            Assert.That(new[] { resumed.NextInt(0, 1000), resumed.NextInt(0, 1000), resumed.NextInt(0, 1000) }, Is.EqualTo(expected));
        }

        [Test]
        public void AStream_DependsOnItsNameOnly_NotOnWhatTheParentDrewBefore()
        {
            var fresh = new DeterministicRandomService(99);
            var used = new DeterministicRandomService(99);
            for (var i = 0; i < 50; i++)
                used.NextDouble();

            var loot = fresh.Fork("loot").NextUInt64();

            Assert.That(used.Fork("loot").NextUInt64(), Is.EqualTo(loot));
            Assert.That(fresh.Fork("enemies").NextUInt64(), Is.Not.EqualTo(loot));
        }

        [Test]
        public void IntegerRanges_HoldAtTheExtremes()
        {
            var random = new DeterministicRandomService(3);

            Assert.That(random.NextInt(5, 5), Is.EqualTo(5));
            for (var i = 0; i < 1000; i++)
            {
                var value = random.NextInt(int.MinValue, int.MaxValue);
                Assert.That(value, Is.LessThan(int.MaxValue));
                Assert.That(random.NextInt(-3, 4), Is.InRange(-3, 3));
            }

            Assert.Throws<ArgumentOutOfRangeException>(() => random.NextInt(2, 1));
        }

        [Test]
        public void AnAllZeroState_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => new DeterministicRandomService(1).RestoreState(default));
        }

        [Test]
        public void WeightedPick_RejectsNonFiniteWeightsAndRolls_AndSkipsTrailingZeroWeight()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeightedItem<int>(1, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeightedItem<int>(1, double.PositiveInfinity));
            var items = new[] { new WeightedItem<int>(7, 1d), new WeightedItem<int>(9, 0d) };

            Assert.Throws<ArgumentOutOfRangeException>(() => WeightedItems.Pick(items, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => WeightedItems.Pick(items, 1d));
            Assert.That(WeightedItems.Pick(items, 0.9999999999999999d), Is.EqualTo(7));
        }
    }
}
