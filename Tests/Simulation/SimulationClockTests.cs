using System;
using System.Collections.Generic;
using Foundation.Simulation;
using Foundation.Timing;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Simulation
{
    public sealed class SimulationClockTests
    {
        [TestCase(1f, 0.2f)]
        [TestCase(0.5f, 0.1f)]
        [TestCase(0f, 0f)]
        public void SimulationDelta_IsTheUnscaledFrameDeltaTimesTheSimulationScale(float scale, float expected)
        {
            var clock = new TimeProviderSimulationClock(new StubTimeProvider(0.2f), new StubTimeController(scale));

            Assert.That(clock.GetDeltaTime(), Is.EqualTo(expected).Within(1e-6f));
        }

        [Test]
        public void Clock_IgnoresThePresentationScaledDelta()
        {
            var time = new StubTimeProvider(unscaledDeltaTime: 0.2f) { DeltaTime = 0f };
            var clock = new TimeProviderSimulationClock(time, new StubTimeController(1f));

            Assert.That(clock.GetDeltaTime(), Is.EqualTo(0.2f).Within(1e-6f),
                "freezing animations must not freeze production");
        }

        [Test]
        public void Runner_AdvancesProportionallyToSimulationScale()
        {
            var deltas = new List<float>();
            var controller = new StubTimeController(1f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                new TimeProviderSimulationClock(new StubTimeProvider(0.2f), controller));
            runner.Start();

            runner.Tick();
            controller.SetSimulationScale(0.5f);
            runner.Tick();
            controller.SetSimulationScale(0f);
            runner.Tick();

            Assert.That(deltas, Is.EqualTo(new[] { 0.2f, 0.1f }));
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.3d).Within(1e-5));
        }

        [Test]
        public void UnityTimeController_RejectsNonFiniteScalesWithoutChangingCurrentValues()
        {
            var originalTimeScale = Time.timeScale;
            try
            {
                var controller = new UnityTimeController();
                controller.SetSimulationScale(0.5f);
                controller.SetAnimationScale(0.25f);

                foreach (var invalidScale in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                {
                    Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetSimulationScale(invalidScale));
                    Assert.That(controller.SimulationScale, Is.EqualTo(0.5f));
                    Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetAnimationScale(invalidScale));
                    Assert.That(controller.AnimationScale, Is.EqualTo(0.25f));
                    Assert.That(Time.timeScale, Is.EqualTo(0.25f));
                }
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        private sealed class DeltaCapturingSystem : ISimulationSystem
        {
            private readonly List<float> _deltas;

            public DeltaCapturingSystem(List<float> deltas) => _deltas = deltas;

            public int Order => 0;

            public void Tick(in SimulationTick tick) => _deltas.Add(tick.DeltaTime);
        }

        private sealed class StubTimeProvider : ITimeProvider
        {
            public StubTimeProvider(float unscaledDeltaTime)
            {
                UnscaledDeltaTime = unscaledDeltaTime;
                DeltaTime = unscaledDeltaTime;
            }

            public DateTime UtcNow => DateTime.UnixEpoch;
            public float Time => 0f;
            public float DeltaTime { get; set; }
            public float UnscaledDeltaTime { get; }
        }

        private sealed class StubTimeController : ITimeController
        {
            public StubTimeController(float simulationScale) => SimulationScale = simulationScale;

            public float SimulationScale { get; private set; }
            public float AnimationScale { get; private set; } = 1f;

            public void SetSimulationScale(float scale) => SimulationScale = scale;

            public void SetAnimationScale(float scale) => AnimationScale = scale;
        }
    }
}
