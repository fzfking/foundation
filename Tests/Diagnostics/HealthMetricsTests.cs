using Foundation.Diagnostics;
using NUnit.Framework;

namespace Foundation.Tests.Diagnostics
{
    public sealed class HealthMetricsTests
    {
        [Test]
        public void SlowFramesAndStalls_AreCountedSeparately_AndNonPlayFramesAreIgnored()
        {
            var frames = new FrameHealthAccumulator();

            frames.Add(0.016f);
            frames.Add(0.016f);
            frames.Add(0.040f); // slow, not a stall
            frames.Add(0.300f); // a stall, and slow
            frames.Add(5f);     // a loading screen or a resume: not play
            frames.Add(0f);

            Assert.That(frames.Frames, Is.EqualTo(4));
            Assert.That(frames.SlowFrames, Is.EqualTo(2));
            Assert.That(frames.Hitches, Is.EqualTo(1));
            Assert.That(frames.SlowFramePercent, Is.EqualTo(50d).Within(0.001));
        }

        [TestCase(12d, "lt20")]
        [TestCase(29.9d, "20_30")]
        [TestCase(30d, "30_45")]
        [TestCase(57d, "45_58")]
        [TestCase(60d, "58_plus")]
        public void Fps_FallsIntoTheRangeItsBoundariesName(double fps, string expected)
            => Assert.That(HealthBuckets.Fps(fps), Is.EqualTo(expected));

        [Test]
        public void MemoryStep_IsCappedSoItNeverGrowsTheNumberOfDistinctValues()
        {
            Assert.That(HealthBuckets.MemoryStepMegabytes(700L * 1024 * 1024), Is.EqualTo(512));
            Assert.That(HealthBuckets.MemoryStepMegabytes(9000L * 1024 * 1024), Is.EqualTo(3072));
        }
    }
}
