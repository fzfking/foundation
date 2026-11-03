using System;
using System.Collections.Generic;
using Foundation.Simulation;
using NUnit.Framework;

namespace Foundation.Tests.Simulation
{
    public sealed class SimulationRunnerTests
    {
        [Test]
        public void Tick_RunsSystemsInDeterministicOrder_RegardlessOfRegistrationOrder()
        {
            var log = new List<string>();
            var clock = new StubClock(0.5f);
            var runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new RecordingSystem("late", 10, log),
                    new RecordingSystem("early", -5, log),
                    new RecordingSystem("middle", 0, log)
                },
                clock);
            runner.Start();

            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "early", "middle", "late" }));
        }

        [Test]
        public void Tick_BeforeStart_DoesNothing()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(new[] { new RecordingSystem("a", 0, log) }, new StubClock(0.5f));

            runner.Tick();

            Assert.That(log, Is.Empty);
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
        }

        [Test]
        public void PauseAndResume_SuspendTimeWithoutCatchUp()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(new[] { new RecordingSystem("a", 0, log) }, new StubClock(0.5f));
            runner.Start();
            runner.Tick();

            runner.Pause();
            runner.Tick();
            runner.Tick();

            Assert.That(log, Has.Count.EqualTo(1));
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.5d).Within(1e-6));

            runner.Resume();
            runner.Tick();

            Assert.That(log, Has.Count.EqualTo(2));
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(1.0d).Within(1e-6),
                "a paused simulation must not accumulate time to replay later");
        }

        [Test]
        public void Stop_ResetsElapsedTimeAndTickIndex()
        {
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new StubClock(0.5f));
            runner.Start();
            runner.Tick();

            runner.Stop();

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void VariableStep_PassesTheWholeFrameDeltaToSystems()
        {
            var deltas = new List<float>();
            var runner = new SimulationRunner(new[] { new DeltaCapturingSystem(deltas) }, new StubClock(0.37f));
            runner.Start();

            runner.Tick();

            Assert.That(runner.IsFixedStep, Is.False);
            Assert.That(deltas, Is.EqualTo(new[] { 0.37f }));
        }

        [Test]
        public void FixedStep_ConsumesWholeStepsAndKeepsTheRemainder()
        {
            var deltas = new List<float>();
            var clock = new StubClock(0.25f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                clock,
                fixedDeltaTime: 0.1f);
            runner.Start();

            runner.Tick();

            Assert.That(runner.IsFixedStep, Is.True);
            Assert.That(deltas, Is.EqualTo(new[] { 0.1f, 0.1f }));

            runner.Tick();

            Assert.That(deltas, Has.Count.EqualTo(5), "the 0.05 remainder must carry into the next frame");
            Assert.That(runner.Context.TickIndex, Is.EqualTo(5));
        }

        [Test]
        public void FixedStep_HugeFrameDelta_StopsAtTheCatchUpLimit()
        {
            var deltas = new List<float>();
            var clock = new MutableClock(10f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                clock,
                fixedDeltaTime: 0.1f);
            runner.Start();

            runner.Tick();

            Assert.That(deltas, Has.Count.EqualTo(SimulationRunner.MaxFixedStepsPerTick));
            Assert.That(runner.Context.ElapsedTime,
                Is.EqualTo(SimulationRunner.MaxFixedStepsPerTick * 0.1d).Within(1e-5));
        }

        [Test]
        public void FixedStep_LeftoverTimeAtTheLimit_IsDroppedNotReplayed()
        {
            var deltas = new List<float>();
            var clock = new MutableClock(10f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                clock,
                fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Tick();
            deltas.Clear();
            Assert.That(runner.Context.DroppedTime, Is.EqualTo(9.2d).Within(1e-4), "the skipped time is reported");

            // A single normal frame follows the hitch. If the 9.2s surplus had been kept, this
            // frame would replay another full batch of steps instead of the one it earned.
            clock.Delta = 0.1f;
            runner.Tick();

            Assert.That(deltas, Is.EqualTo(new[] { 0.1f }));
        }

        // A colony sim at x5 on a 20 fps device needs 15 steps of 1/60 s a frame; the default budget of 8 would lose
        // almost half of the simulated time every frame.
        [Test]
        public void FixedStep_AStepBudgetForFastForward_RunsEveryStepTheFrameEarned()
        {
            var deltas = new List<float>();
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                new StubClock(0.26f),
                fixedDeltaTime: 1f / 60f,
                maxStepsPerTick: 16);
            runner.Start();

            runner.Tick();

            Assert.That(deltas, Has.Count.EqualTo(15));
            Assert.That(runner.Context.DroppedTime, Is.Zero);
        }

        [Test]
        public void FixedStep_CatchUp_CarriesALimitedBacklogIntoTheNextFrames_AndReportsTheRest()
        {
            var deltas = new List<float>();
            var clock = new MutableClock(1f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                clock,
                fixedDeltaTime: 0.1f,
                maxStepsPerTick: 2,
                maxCatchUpTime: 0.3f);
            runner.Start();

            runner.Tick();
            Assert.That(deltas, Has.Count.EqualTo(2));
            Assert.That(runner.Context.DroppedTime, Is.EqualTo(0.5d).Within(1e-4), "0.8 s left, 0.3 s of it kept");

            clock.Delta = 0.1f;
            runner.Tick();
            runner.Tick();

            // 0.3 s kept plus 0.1 s a frame: each frame runs its full budget until the backlog is gone.
            Assert.That(deltas, Has.Count.EqualTo(6));
            Assert.That(runner.Context.DroppedTime, Is.EqualTo(0.5d).Within(1e-4));
        }

        [Test]
        public void Tick_WithoutElapsedTime_DoesNotAdvance()
        {
            var deltas = new List<float>();
            var runner = new SimulationRunner(new[] { new DeltaCapturingSystem(deltas) }, new StubClock(0f));
            runner.Start();

            runner.Tick();

            Assert.That(deltas, Is.Empty);
            Assert.That(runner.Context.TickIndex, Is.Zero);
        }

        [Test]
        public void Tick_WhenSystemThrows_StopsWithoutAdvancingTheFailedTick()
        {
            var firstSystemCalls = 0;
            var runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () => firstSystemCalls++),
                    new CallbackSystem(1, () => throw new InvalidOperationException("system failed"))
                },
                new StubClock(0.5f));
            runner.Start();

            Assert.Throws<InvalidOperationException>(runner.Tick);
            runner.Tick();

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
            Assert.That(firstSystemCalls, Is.EqualTo(1));
        }

        [Test]
        public void Runner_RejectsNonFiniteSettingsAndClockDelta()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationRunner(
                Array.Empty<ISimulationSystem>(), new StubClock(0f), fixedDeltaTime: float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationRunner(
                Array.Empty<ISimulationSystem>(), new StubClock(0f), maxCatchUpTime: float.PositiveInfinity));

            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new StubClock(float.NaN));
            runner.Start();
            Assert.Throws<InvalidOperationException>(runner.Tick);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void ManualTickMode_RequiresAFixedDeltaTime()
        {
            Assert.Throws<ArgumentException>(() => new SimulationRunner(
                Array.Empty<ISimulationSystem>(),
                new StubClock(0f),
                tickMode: SimulationTickMode.Manual));
        }

        [Test]
        public void ManualTickMode_IgnoresTheFrameTick()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(
                new[] { new RecordingSystem("a", 0, log) },
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();

            runner.Tick();

            Assert.That(log, Is.Empty);
            Assert.That(runner.Context.TickIndex, Is.Zero);
        }

        [Test]
        public void ManualStep_RunsExactlyTheRequestedWholeSteps()
        {
            var deltas = new List<float>();
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                new ThrowingClock(),
                fixedDeltaTime: 0.25f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();

            runner.Step(3);

            Assert.That(deltas, Is.EqualTo(new[] { 0.25f, 0.25f, 0.25f }));
            Assert.That(runner.Context.TickIndex, Is.EqualTo(3));
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.75d).Within(1e-6));
        }

        [Test]
        public void ManualStep_InAutomaticMode_IsRejected()
        {
            var runner = new SimulationRunner(
                Array.Empty<ISimulationSystem>(),
                new StubClock(0.1f),
                fixedDeltaTime: 0.1f);
            runner.Start();

            Assert.Throws<InvalidOperationException>(() => runner.Step());
            Assert.That(runner.Context.TickIndex, Is.Zero);
        }

        [Test]
        public void ManualStep_WhilePaused_DoesNothingUntilResume()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(
                new[] { new RecordingSystem("a", 0, log) },
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();

            runner.Pause();
            runner.Step(3);

            Assert.That(log, Is.Empty);
            Assert.That(runner.Context.TickIndex, Is.Zero);

            runner.Resume();
            runner.Step(2);

            Assert.That(log, Has.Count.EqualTo(2));
            Assert.That(runner.Context.TickIndex, Is.EqualTo(2));
        }

        [Test]
        public void ManualStep_AfterStop_DoesNothingUntilStart()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(
                new[] { new RecordingSystem("a", 0, log) },
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();
            runner.Step(2);

            runner.Stop();
            runner.Step(4);

            Assert.That(log, Has.Count.EqualTo(2));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);

            runner.Start();
            runner.Step();

            Assert.That(log, Has.Count.EqualTo(3));
            Assert.That(runner.Context.TickIndex, Is.EqualTo(1));
        }

        [Test]
        public void ManualStep_WhenSystemThrows_StopsWithoutAdvancingTheFailedTick()
        {
            var firstSystemCalls = 0;
            var runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () => firstSystemCalls++),
                    new CallbackSystem(1, () => throw new InvalidOperationException("system failed"))
                },
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();

            Assert.Throws<InvalidOperationException>(() => runner.Step(2));
            runner.Step(2);

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
            Assert.That(firstSystemCalls, Is.EqualTo(1));
        }

        [Test]
        public void FixedStep_PreserveBacklog_KeepsUnrunStepsWithoutDroppingThem()
        {
            var deltas = new List<float>();
            var clock = new MutableClock(1f);
            var runner = new SimulationRunner(
                new[] { new DeltaCapturingSystem(deltas) },
                clock,
                fixedDeltaTime: 0.1f,
                maxStepsPerTick: 2,
                backlogPolicy: SimulationBacklogPolicy.Preserve);
            runner.Start();

            runner.Tick();

            Assert.That(deltas, Has.Count.EqualTo(2), "the per-frame budget still bounds one frame");
            Assert.That(runner.Context.DroppedTime, Is.Zero);

            clock.Delta = 0.1f;
            runner.Tick();

            Assert.That(deltas, Has.Count.EqualTo(4),
                "the 0.8 s backlog plus the new 0.1 s keeps the next frame at its full budget");
            Assert.That(runner.Context.DroppedTime, Is.Zero);
        }

        [Test]
        public void Tick_SameOrderAndType_KeepsRegistrationOrder()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new RecordingSystem("first", 0, log),
                    new RecordingSystem("second", 0, log),
                    new RecordingSystem("third", 0, log)
                },
                new StubClock(0.1f));
            runner.Start();

            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "first", "second", "third" }));
        }

        [Test]
        public void FixedStep_PauseFromSystem_FinishesTheCurrentStepAndStopsTheBatch()
        {
            SimulationRunner runner = null;
            var clock = new MutableClock(0.1f);
            var indices = new List<long>();
            var calls = 0;
            runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () =>
                    {
                        if (++calls == 2)
                            runner.Pause();
                    }),
                    new CallbackSystem(1, () => indices.Add(runner.Context.TickIndex))
                },
                clock,
                fixedDeltaTime: 0.1f);
            runner.Start();

            runner.Tick();
            Assert.That(indices, Is.EqualTo(new[] { 0L }));

            clock.Delta = 0.5f;
            runner.Tick();

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Paused));
            Assert.That(indices, Is.EqualTo(new[] { 0L, 1L }),
                "the current step finishes after the pause request and the batch stops");
            Assert.That(runner.Context.TickIndex, Is.EqualTo(2),
                "the finished step is committed once");

            runner.Resume();
            clock.Delta = 0.1f;
            runner.Tick();

            Assert.That(indices, Is.EqualTo(new[] { 0L, 1L, 2L, 3L, 4L, 5L, 6L }),
                "resuming continues with the next index and never repeats one");
        }

        [Test]
        public void PauseFromSystem_StopsTheBatch_EvenWhenAResumeFollowsInTheSameStep()
        {
            SimulationRunner runner = null;
            var indices = new List<long>();
            runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () => runner.Pause()),
                    new CallbackSystem(1, () => runner.Resume()),
                    new CallbackSystem(2, () => indices.Add(runner.Context.TickIndex))
                },
                new StubClock(0.5f),
                fixedDeltaTime: 0.1f);
            runner.Start();

            runner.Tick();

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Running));
            Assert.That(indices, Is.EqualTo(new[] { 0L }), "the requested pause still stops the batch");
            Assert.That(runner.Context.TickIndex, Is.EqualTo(1));
        }

        [Test]
        public void FixedStep_StopFromSystem_AbortsTheBatchAndKeepsTickIndexAtZero()
        {
            SimulationRunner runner = null;
            var stopCalls = 0;
            var afterStopCalls = 0;
            var clock = new MutableClock(0.1f);
            runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () =>
                    {
                        if (++stopCalls == 2)
                            runner.Stop();
                    }),
                    new CallbackSystem(1, () => afterStopCalls++)
                },
                clock,
                fixedDeltaTime: 0.1f);
            runner.Start();

            runner.Tick();
            Assert.That(runner.Context.TickIndex, Is.EqualTo(1));
            Assert.That(afterStopCalls, Is.EqualTo(1));

            clock.Delta = 0.5f;
            runner.Tick();

            Assert.That(stopCalls, Is.EqualTo(2));
            Assert.That(afterStopCalls, Is.EqualTo(1), "systems after Stop do not run in the aborted step");
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero, "Stop must not be followed by an index increment");
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void ManualStep_StopFromSystem_StopsRemainingStepsAndKeepsTickIndexAtZero()
        {
            SimulationRunner runner = null;
            var stopCalls = 0;
            var afterStopCalls = 0;
            runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () =>
                    {
                        if (++stopCalls == 2)
                            runner.Stop();
                    }),
                    new CallbackSystem(1, () => afterStopCalls++)
                },
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();

            runner.Step();
            Assert.That(runner.Context.TickIndex, Is.EqualTo(1));
            Assert.That(afterStopCalls, Is.EqualTo(1));

            runner.Step(5);

            Assert.That(stopCalls, Is.EqualTo(2));
            Assert.That(afterStopCalls, Is.EqualTo(1), "systems after Stop do not run in the aborted step");
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void SystemStopThenThrow_KeepsTheResetAndRethrows()
        {
            var failure = new InvalidOperationException("system failed after stop");
            SimulationRunner runner = null;
            var calls = 0;
            runner = new SimulationRunner(
                new[]
                {
                    new CallbackSystem(0, () =>
                    {
                        if (++calls != 2)
                            return;

                        runner.Stop();
                        throw failure;
                    })
                },
                new StubClock(0.1f),
                fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Tick();
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.1d).Within(1e-6));

            var thrown = Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void SystemStartThenThrow_KeepsTheResetAndRethrows()
        {
            var failure = new InvalidOperationException("system failed after start");
            SimulationRunner runner = null;
            var calls = 0;
            runner = new SimulationRunner(
                new[]
                {
                    new CallbackSystem(0, () =>
                    {
                        if (++calls != 2)
                            return;

                        runner.Start();
                        throw failure;
                    })
                },
                new StubClock(0.1f),
                fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Tick();
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.1d).Within(1e-6));

            var thrown = Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void ReentrantTickFromSystem_ThrowsWithoutAdvancing()
        {
            SimulationRunner runner = null;
            var calls = 0;
            runner = new SimulationRunner(
                new[] { new CallbackSystem(0, () => { calls++; runner.Tick(); }) },
                new StubClock(0.1f),
                fixedDeltaTime: 0.1f);
            runner.Start();

            Assert.Throws<InvalidOperationException>(runner.Tick);
            runner.Tick();

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void Runner_PreservesTheLegacySixArgumentConstructor()
        {
            var constructor = typeof(SimulationRunner).GetConstructor(new[]
            {
                typeof(IEnumerable<ISimulationSystem>),
                typeof(ISimulationClock),
                typeof(SimulationContext),
                typeof(float),
                typeof(int),
                typeof(float)
            });

            Assert.That(constructor, Is.Not.Null);
        }

        [Test]
        public void LegacyConstructorShape_KeepsAutomaticBoundedDefaults()
        {
            var runner = new SimulationRunner(
                Array.Empty<ISimulationSystem>(),
                new StubClock(0f),
                null,
                0.1f,
                SimulationRunner.MaxFixedStepsPerTick,
                0f);

            Assert.That(runner.TickMode, Is.EqualTo(SimulationTickMode.Automatic));
            Assert.That(runner.BacklogPolicy, Is.EqualTo(SimulationBacklogPolicy.Bounded));
        }

        private sealed class MutableClock : ISimulationClock
        {
            public MutableClock(float delta) => Delta = delta;

            public float Delta { get; set; }

            public float GetDeltaTime() => Delta;
        }

        private sealed class StubClock : ISimulationClock
        {
            private readonly float _delta;

            public StubClock(float delta) => _delta = delta;

            public float GetDeltaTime() => _delta;
        }

        private sealed class ThrowingClock : ISimulationClock
        {
            public float GetDeltaTime() => throw new InvalidOperationException("the clock must not be read");
        }

        private sealed class RecordingSystem : ISimulationSystem
        {
            private readonly string _name;
            private readonly List<string> _log;

            public RecordingSystem(string name, int order, List<string> log)
            {
                _name = name;
                Order = order;
                _log = log;
            }

            public int Order { get; }

            public void Tick(in SimulationTick tick) => _log.Add(_name);
        }

        private sealed class DeltaCapturingSystem : ISimulationSystem
        {
            private readonly List<float> _deltas;

            public DeltaCapturingSystem(List<float> deltas) => _deltas = deltas;

            public int Order => 0;

            public void Tick(in SimulationTick tick) => _deltas.Add(tick.DeltaTime);
        }

        private sealed class CallbackSystem : ISimulationSystem
        {
            private readonly Action _callback;

            public CallbackSystem(int order, Action callback)
            {
                Order = order;
                _callback = callback;
            }

            public int Order { get; }

            public void Tick(in SimulationTick tick) => _callback();
        }
    }
}
