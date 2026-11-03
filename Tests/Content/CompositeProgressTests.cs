using System;
using Foundation.Content;
using NUnit.Framework;

namespace Foundation.Tests.Content
{
    public sealed class CompositeProgressTests
    {
        private sealed class RecordingProgress : IProgress<float>
        {
            public float LastValue { get; private set; } = -1f;
            public void Report(float value) => LastValue = value;
        }

        [Test]
        public void EqualWeights_AverageTheTwoSlots()
        {
            var target = new RecordingProgress();
            var composite = new CompositeProgress(target, new[] { 1f, 1f });

            composite.Slot(0).Report(1f);
            composite.Slot(1).Report(0f);

            Assert.That(target.LastValue, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void UnequalWeights_WeightTheHeavierSlotMore()
        {
            var target = new RecordingProgress();
            var composite = new CompositeProgress(target, new[] { 3f, 1f });

            composite.Slot(0).Report(1f);
            composite.Slot(1).Report(0f);

            Assert.That(target.LastValue, Is.EqualTo(0.75f).Within(0.0001f));
        }

        [Test]
        public void AllSlotsComplete_ReportsOne()
        {
            var target = new RecordingProgress();
            var composite = new CompositeProgress(target, new[] { 2f, 5f, 1f });

            composite.Slot(0).Report(1f);
            composite.Slot(1).Report(1f);
            composite.Slot(2).Report(1f);

            Assert.That(target.LastValue, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void OutOfRangeValues_AreClamped()
        {
            var target = new RecordingProgress();
            var composite = new CompositeProgress(target, new[] { 1f });

            composite.Slot(0).Report(5f);
            Assert.That(target.LastValue, Is.EqualTo(1f));

            composite.Slot(0).Report(-5f);
            Assert.That(target.LastValue, Is.EqualTo(0f));
        }

        [Test]
        public void NonFiniteWeightsAndProgress_AreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeProgress(null, new[] { float.NaN }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CompositeProgress(null, new[] { float.PositiveInfinity }));

            var progress = new CompositeProgress(null, new[] { 1f });
            Assert.Throws<ArgumentOutOfRangeException>(() => progress.Slot(0).Report(float.NaN));
        }
    }
}
