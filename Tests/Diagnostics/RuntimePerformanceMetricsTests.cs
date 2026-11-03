#if UNITY_EDITOR || DEVELOPMENT_BUILD || FOUNDATION_DEVELOPMENT
using System;
using Foundation.Diagnostics;
using NUnit.Framework;
using System.Reflection;
using Unity.Profiling;

namespace Foundation.Tests.Diagnostics
{
    public sealed class RuntimePerformanceMetricsTests
    {
        [Test]
        public void FrameWindow_UsesElapsedTimeForAverage_AndIncludesSlowestFrame()
        {
            var window = new FramePerformanceWindow();
            window.Add(0.025f);
            window.Add(0.05f);
            var result = window.Read();
            Assert.That(result.FramesPerSecond, Is.EqualTo(20).Within(0.001));
            Assert.That(result.FrameMilliseconds, Is.EqualTo(50).Within(0.001));
            Assert.That(result.AverageFramesPerSecond, Is.EqualTo(26.666666).Within(0.001));
            Assert.That(result.MinimumFramesPerSecond, Is.EqualTo(20).Within(0.001));
            Assert.That(result.MaximumFramesPerSecond, Is.EqualTo(40).Within(0.001));
        }

        [Test]
        public void FrameWindow_ExpiresOldHitch_AndBoundsHighFrameRates()
        {
            var window = new FramePerformanceWindow(3, 4);
            window.Add(2);
            window.Add(1);
            window.Add(1);
            window.Add(1);
            Assert.That(window.Read().Frames, Is.EqualTo(3));
            Assert.That(window.Read().MinimumFramesPerSecond, Is.EqualTo(1));
            for (var index = 0; index < 10; index++)
                window.Add(0.01f);
            Assert.That(window.Read().Frames, Is.EqualTo(4));
            Assert.That(window.Read().AverageFramesPerSecond, Is.EqualTo(100).Within(0.001));
        }

        [Test]
        public void InvalidFrameSamples_DoNotCorruptStatistics_AndClearRemovesOldSession()
        {
            var window = new FramePerformanceWindow();
            window.Add(0.1f);
            window.Add(0);
            window.Add(-1);
            window.Add(float.NaN);
            window.Add(float.PositiveInfinity);
            Assert.That(window.Read().Frames, Is.EqualTo(1));
            window.Clear();
            Assert.That(window.Read().Frames, Is.Zero);
            Assert.That(window.Read().AverageFramesPerSecond, Is.Zero);
        }

        [Test]
        public void SamplingAndReadingFrameWindow_DoNotAllocateAfterConstruction()
        {
            var window = new FramePerformanceWindow();
            window.Add(0.01f);
            window.Read();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < 1000; index++)
            {
                window.Add(0.01f);
                window.Read();
            }
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        [Test]
        public void RecorderLifetime_StopAndDisposeAreIdempotent_AndRestartBeginsFreshWindow()
        {
            using var metrics = new RuntimePerformanceMetrics();
            metrics.Start();
            metrics.Tick(0.1f);
            var count = metrics.ActiveRecorderCount;
            metrics.Start();
            Assert.That(metrics.ActiveRecorderCount, Is.EqualTo(count));
            Assert.That(metrics.ReadFrames().Frames, Is.EqualTo(1));
            metrics.Stop();
            metrics.Stop();
            var recorders = (ProfilerRecorder[])typeof(RuntimePerformanceMetrics)
                .GetField("_recorders", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(metrics);
            foreach (var recorder in recorders)
                Assert.That(recorder.Valid, Is.False, "Stopped tools must not retain native recorder handles.");
            Assert.That(metrics.IsRunning, Is.False);
            Assert.That(metrics.ActiveRecorderCount, Is.Zero);
            Assert.That(metrics.Read(PerformanceMetric.ManagedUsed), Is.Null);
            metrics.Start();
            Assert.That(metrics.ReadFrames().Frames, Is.Zero);
            metrics.Dispose();
            Assert.That(metrics.IsRunning, Is.False);
        }
    }
}
#endif
