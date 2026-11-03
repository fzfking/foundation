using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.Simulation;
using Foundation.Timing;
using NUnit.Framework;

namespace Foundation.Tests.Simulation
{
    public sealed class SimulationTimersTests
    {
        [Test]
        public void Timer_FiresAfterItsWholeTickDelay_RegardlessOfFrameDelta()
        {
            var clock = new MutableClock(100f);
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), clock);
            runner.Start();
            var firedAt = new List<long>();
            runner.Timers.Schedule(1, () => firedAt.Add(runner.Context.TickIndex));

            runner.Tick();

            Assert.That(firedAt, Is.Empty, "one tick is skipped before the timer is due");

            clock.Delta = 0.001f;
            runner.Tick();

            Assert.That(firedAt, Is.EqualTo(new[] { 1L }), "the frame delta must not decide when a timer fires");
        }

        [Test]
        public void Timer_WithZeroDelay_RunsOnTheNextTick()
        {
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var firedAt = new List<long>();
            runner.Timers.Schedule(0, () => firedAt.Add(runner.Context.TickIndex));

            runner.Tick();

            Assert.That(firedAt, Is.EqualTo(new[] { 0L }));
        }

        [Test]
        public void Timers_DueOnTheSameTick_RunInSchedulingOrder()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            runner.Timers.Schedule(1, () => log.Add("first"));
            runner.Timers.Schedule(1, () => log.Add("second"));
            runner.Timers.Schedule(1, () => log.Add("third"));

            runner.Tick();
            Assert.That(log, Is.Empty);

            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "first", "second", "third" }));
        }

        [Test]
        public void Timer_Dispose_CancelsItWithoutAffectingTheRun()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var handle = runner.Timers.Schedule(0, () => fired++);

            handle.Dispose();
            runner.Tick();

            Assert.That(handle.IsActive, Is.False);
            Assert.That(fired, Is.Zero);
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Running));
        }

        [Test]
        public void Timer_ScheduledFromACallback_DoesNotRunInTheSameTick()
        {
            var firedAt = new List<long>();
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            runner.Timers.Schedule(0, () =>
            {
                firedAt.Add(runner.Context.TickIndex);
                runner.Timers.Schedule(0, () => firedAt.Add(runner.Context.TickIndex));
            });

            runner.Tick();

            Assert.That(firedAt, Is.EqualTo(new[] { 0L }));

            runner.Tick();

            Assert.That(firedAt, Is.EqualTo(new[] { 0L, 1L }));
        }

        [Test]
        public void StopAndStart_ClearScheduledTimers()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var stoppedHandle = runner.Timers.Schedule(0, () => fired++);

            runner.Stop();

            Assert.That(stoppedHandle.IsActive, Is.False);

            runner.Start();
            var restartedHandle = runner.Timers.Schedule(0, () => fired++);

            runner.Start();

            Assert.That(restartedHandle.IsActive, Is.False);
            runner.Tick();
            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void Clear_DropsTimers_AndTheGameCanScheduleAgain()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var clearedHandle = runner.Timers.Schedule(0, () => fired++);

            runner.Timers.Clear();
            runner.Tick();

            Assert.That(clearedHandle.IsActive, Is.False);
            Assert.That(fired, Is.Zero);

            runner.Timers.Schedule(0, () => fired++);
            runner.Tick();

            Assert.That(fired, Is.EqualTo(1), "a timer registered after a clear runs normally");
        }

        [Test]
        public void Pause_KeepsTimers_AndResumeContinuesTheirTicks()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            runner.Timers.Schedule(0, () => fired++);

            runner.Pause();
            runner.Tick();

            Assert.That(fired, Is.Zero);

            runner.Resume();
            runner.Tick();

            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ManualStep_AdvancesTimers()
        {
            var firedAt = new List<long>();
            var runner = new SimulationRunner(
                Array.Empty<ISimulationSystem>(),
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();
            runner.Timers.Schedule(1, () => firedAt.Add(runner.Context.TickIndex));

            runner.Step();
            Assert.That(firedAt, Is.Empty);

            runner.Step();
            Assert.That(firedAt, Is.EqualTo(new[] { 1L }));
        }

        [Test]
        public void TimerThatThrows_StopsTheRunWithoutAdvancingTheFailedTick()
        {
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var handle = runner.Timers.Schedule(0, () => throw new InvalidOperationException("timer failed"));

            Assert.Throws<InvalidOperationException>(runner.Tick);
            runner.Tick();

            Assert.That(handle.IsActive, Is.False);
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void Timers_RunAfterTheSystemsOfTheSameTick()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(new[] { new RecordingSystem(log) }, new MutableClock(0.1f));
            runner.Start();
            runner.Timers.Schedule(0, () => log.Add("timer"));

            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "system", "timer" }));
        }

        [Test]
        public void Timer_ScheduledFromASystemInItsTick_RunsOnTheNextTick()
        {
            var log = new List<string>();
            SimulationRunner runner = null;
            var scheduled = false;
            runner = new SimulationRunner(
                new ISimulationSystem[]
                {
                    new CallbackSystem(0, () =>
                    {
                        log.Add("system");
                        if (scheduled)
                            return;
                        scheduled = true;
                        runner.Timers.Schedule(0, () => log.Add("timer"));
                    })
                },
                new MutableClock(0.1f));
            runner.Start();

            runner.Tick();
            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "system", "system", "timer" }),
                "a timer scheduled by a system in its tick belongs to the next tick");
        }

        [Test]
        public void Timer_ScheduledBeforeStart_IsDiscardedByStart()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            var handle = runner.Timers.Schedule(0, () => fired++);

            runner.Start();
            runner.Tick();

            Assert.That(handle.IsActive, Is.False);
            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void StopFromSystem_DiscardsTimersOfTheAbortedStep()
        {
            var fired = 0;
            SimulationRunner runner = null;
            runner = new SimulationRunner(
                new[] { new CallbackSystem(0, () => runner.Stop()) },
                new MutableClock(0.1f));
            runner.Start();
            var handle = runner.Timers.Schedule(0, () => fired++);

            runner.Tick();

            Assert.That(handle.IsActive, Is.False);
            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void StopFromTimer_AbortsTheBatchAndKeepsTickIndexAtZero()
        {
            SimulationRunner runner = null;
            var systemCalls = 0;
            var laterTimerFired = false;
            runner = new SimulationRunner(
                new[] { new CallbackSystem(0, () => systemCalls++) },
                new MutableClock(0.5f),
                fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Timers.Schedule(0, () => runner.Stop());
            runner.Timers.Schedule(0, () => laterTimerFired = true);

            runner.Tick();

            Assert.That(systemCalls, Is.EqualTo(1), "the current step reached the timer phase");
            Assert.That(laterTimerFired, Is.False, "Stop clears the remaining due timers of the step");
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero, "Stop must not be followed by an index increment");
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void PauseFromTimer_FinishesTheCurrentStepAndStopsTheBatch()
        {
            SimulationRunner runner = null;
            var systemCalls = 0;
            var timerCalls = 0;
            runner = new SimulationRunner(
                new[] { new CallbackSystem(0, () => systemCalls++) },
                new MutableClock(0.5f),
                fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Timers.Schedule(0, () => { timerCalls++; runner.Pause(); });
            runner.Timers.Schedule(0, () => timerCalls++);

            runner.Tick();

            Assert.That(systemCalls, Is.EqualTo(1), "the current step finishes after the pause request");
            Assert.That(timerCalls, Is.EqualTo(2), "remaining due timers belong to the finished step");
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Paused));
            Assert.That(runner.Context.TickIndex, Is.EqualTo(1), "the finished step is committed once");
        }

        [Test]
        public void TimerStopThenThrow_KeepsTheResetAndRethrows()
        {
            var failure = new InvalidOperationException("timer failed after stop");
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f), fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Tick();
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.1d).Within(1e-6));
            runner.Timers.Schedule(0, () =>
            {
                runner.Stop();
                throw failure;
            });

            var thrown = Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void TimerStartThenThrow_KeepsTheResetAndRethrows()
        {
            var failure = new InvalidOperationException("timer failed after start");
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f), fixedDeltaTime: 0.1f);
            runner.Start();
            runner.Tick();
            Assert.That(runner.Context.ElapsedTime, Is.EqualTo(0.1d).Within(1e-6));
            runner.Timers.Schedule(0, () =>
            {
                runner.Start();
                throw failure;
            });

            var thrown = Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(thrown, Is.SameAs(failure));
            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void ReentrantStepFromTimer_ThrowsWithoutAdvancing()
        {
            SimulationRunner runner = null;
            runner = new SimulationRunner(
                Array.Empty<ISimulationSystem>(),
                new ThrowingClock(),
                fixedDeltaTime: 0.1f,
                tickMode: SimulationTickMode.Manual);
            runner.Start();
            runner.Timers.Schedule(0, () => runner.Step());

            Assert.Throws<InvalidOperationException>(() => runner.Step());

            Assert.That(runner.Context.State, Is.EqualTo(SimulationState.Stopped));
            Assert.That(runner.Context.TickIndex, Is.Zero);
            Assert.That(runner.Context.ElapsedTime, Is.Zero);
        }

        [Test]
        public void SystemException_ClearsScheduledTimersAndTheirHandles()
        {
            var fired = 0;
            var runner = new SimulationRunner(
                new[] { new CallbackSystem(0, () => throw new InvalidOperationException("system failed")) },
                new MutableClock(0.1f));
            runner.Start();
            var future = runner.Timers.Schedule(5, () => fired++);
            var due = runner.Timers.Schedule(0, () => fired++);

            Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(future.IsActive, Is.False);
            Assert.That(due.IsActive, Is.False);
            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void TimerException_ClearsOtherDueTimersAndTheirHandles()
        {
            var otherFired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var throwing = runner.Timers.Schedule(0, () => throw new InvalidOperationException("timer failed"));
            var other = runner.Timers.Schedule(0, () => otherFired++);

            Assert.Throws<InvalidOperationException>(runner.Tick);

            Assert.That(throwing.IsActive, Is.False);
            Assert.That(other.IsActive, Is.False);
            Assert.That(otherFired, Is.Zero);
        }

        [Test]
        public void ClearDuringADueBatch_CancelsTheRestAndReleasesTheirHandles()
        {
            var log = new List<string>();
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            var first = runner.Timers.Schedule(0, () => { log.Add("first"); runner.Timers.Clear(); });
            var second = runner.Timers.Schedule(0, () => log.Add("second"));

            runner.Tick();

            Assert.That(log, Is.EqualTo(new[] { "first" }));
            Assert.That(first.IsActive, Is.False);
            Assert.That(second.IsActive, Is.False);
        }

        [Test]
        public void CancellingManyTimersWhilePaused_ReleasesThemWithoutFiring()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();
            runner.Pause();
            var handles = new List<ISimulationTimerHandle>();
            for (var index = 0; index < 1000; index++)
                handles.Add(runner.Timers.Schedule(1, () => fired++));

            foreach (var handle in handles)
                handle.Dispose();

            runner.Resume();
            runner.Tick();
            runner.Tick();

            Assert.That(fired, Is.Zero);
            Assert.That(handles.TrueForAll(handle => !handle.IsActive), Is.True);

            runner.Timers.Schedule(0, () => fired++);
            runner.Tick();

            Assert.That(fired, Is.EqualTo(1), "the timer collection still works after mass cancellation");
        }

        [Test]
        public void Schedule_RejectsADueTickOverflowInsteadOfFiringImmediately()
        {
            var fired = 0;
            var runner = new SimulationRunner(Array.Empty<ISimulationSystem>(), new MutableClock(0.1f));
            runner.Start();

            Assert.Throws<ArgumentOutOfRangeException>(() => runner.Timers.Schedule(long.MaxValue, () => fired++));
            runner.Tick();

            Assert.That(fired, Is.Zero);
        }

        [Test]
        public void Schedule_RejectsNegativeDelaysAndMissingCallbacks()
        {
            var timers = new SimulationTimers();

            Assert.Throws<ArgumentOutOfRangeException>(() => timers.Schedule(-1, () => { }));
            Assert.Throws<ArgumentNullException>(() => timers.Schedule(0, null));
        }

        [Test]
        public void UseSimulation_RegistersTheTimerServiceTheRunnerTicks()
        {
            using var scope = MinimalServiceScope.Create(registry => registry
                .UseTime()
                .UseSimulation(fixedDeltaTime: 0.1f));

            Assert.That(scope.Resolve<SimulationRunner>().Timers, Is.SameAs(scope.Resolve<SimulationTimers>()));
        }

        [Test]
        public void UseSimulation_PreservesTheLegacyFourArgumentOverload()
        {
            var method = typeof(SimulationServiceRegistryExtensions).GetMethod(
                nameof(SimulationServiceRegistryExtensions.UseSimulation),
                new[] { typeof(IServiceRegistry), typeof(float), typeof(int), typeof(float) });

            Assert.That(method, Is.Not.Null);
        }

        [Test]
        public void LegacyUseSimulationShape_KeepsAutomaticBoundedDefaults()
        {
            using var scope = MinimalServiceScope.Create(registry => registry
                .UseTime()
                .UseSimulation(0.1f, SimulationRunner.MaxFixedStepsPerTick, 0f));

            var runner = scope.Resolve<SimulationRunner>();
            Assert.That(runner.TickMode, Is.EqualTo(SimulationTickMode.Automatic));
            Assert.That(runner.BacklogPolicy, Is.EqualTo(SimulationBacklogPolicy.Bounded));
        }

        private sealed class RecordingSystem : ISimulationSystem
        {
            private readonly List<string> _log;

            public RecordingSystem(List<string> log) => _log = log;

            public int Order => 0;

            public void Tick(in SimulationTick tick) => _log.Add("system");
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

        private sealed class MutableClock : ISimulationClock
        {
            public MutableClock(float delta) => Delta = delta;

            public float Delta { get; set; }

            public float GetDeltaTime() => Delta;
        }

        private sealed class ThrowingClock : ISimulationClock
        {
            public float GetDeltaTime() => throw new InvalidOperationException("the clock must not be read");
        }
    }
}
