using System;
using Foundation.Scheduling;
using Foundation.Timing;
using NUnit.Framework;

namespace Foundation.Tests.Scheduling
{
    public sealed class SchedulerTests
    {
        private sealed class FakeTimeProvider : ITimeProvider
        {
            public DateTime UtcNow { get; set; } = DateTime.UtcNow;
            public float Time { get; set; }
            public float DeltaTime { get; set; }
            public float UnscaledDeltaTime { get; set; }
        }

        [Test]
        public void ScheduleOnce_FiresAfterAccumulatedDelayIsReached()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleOnce(1f, () => fired++);

            time.UnscaledDeltaTime = 0.5f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(0));

            time.UnscaledDeltaTime = 0.6f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1), "a one-shot callback must not fire twice");
        }

        [Test]
        public void ScheduleRepeating_FiresOncePerIntervalWithoutBurstingAfterAHitch()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleRepeating(1f, () => fired++);

            time.UnscaledDeltaTime = 5f; // a large hitch, several intervals' worth
            scheduler.Tick();

            Assert.That(fired, Is.EqualTo(1), "one big delta must fire once, not catch up in a burst");
        }

        [Test]
        public void DisposingHandle_StopsFurtherCallbacks()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            var handle = scheduler.ScheduleRepeating(1f, () => fired++);

            time.UnscaledDeltaTime = 1f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));

            handle.Dispose();
            scheduler.Tick();
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(handle.IsActive, Is.False);
        }

        [Test]
        public void IntegrityMonitor_ReceivesBothDeltasEachTickAfterTheFirst()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.1f };
            TimeSpan? observedReal = null;
            float? observedSimulated = null;
            var monitor = new RecordingMonitor((sim, real) =>
            {
                observedSimulated = sim;
                observedReal = real;
            });
            var scheduler = new Scheduler(time, monitor);

            scheduler.Tick();
            Assert.That(observedReal, Is.Null, "first tick only establishes the baseline");

            time.UtcNow = time.UtcNow.AddSeconds(1);
            scheduler.Tick();

            Assert.That(observedSimulated, Is.EqualTo(0.1f));
            Assert.That(observedReal, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        // Regression: one throwing callback used to end the tick, so every callback after it in the list was skipped.
        [Test]
        public void AThrowingCallback_DoesNotKeepTheOthersFromRunning_AndItsFailureIsReported()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleOnce(0.5f, () => fired++);
            scheduler.ScheduleOnce(0.5f, () => throw new InvalidOperationException("broken timer"));
            scheduler.ScheduleOnce(0.5f, () => fired++);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick());
            Assert.That(fired, Is.EqualTo(2));

            Assert.That(() => scheduler.Tick(), Throws.Nothing, "the failed one-shot does not fire again");
        }

        [Test]
        public void AScopedScheduler_CancelsWhatItScheduled_WhenItsScopeEnds()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var context = new ScopedScheduler(scheduler);
            var contextFired = 0;
            var applicationFired = 0;
            context.ScheduleRepeating(1f, () => contextFired++);
            scheduler.ScheduleRepeating(1f, () => applicationFired++);
            scheduler.Tick();

            context.Dispose();
            scheduler.Tick();

            Assert.That(contextFired, Is.EqualTo(1));
            Assert.That(applicationFired, Is.EqualTo(2), "the application's own timers keep running");
            Assert.Throws<ObjectDisposedException>(() => context.ScheduleOnce(1f, () => { }));
        }

        [Test]
        public void DisposingFromACallback_StopsTheTickWithoutAccessingClearedEntries()
        {
            var scheduler = new Scheduler(new FakeTimeProvider { UnscaledDeltaTime = 1f });
            var fired = false;
            scheduler.ScheduleOnce(0f, () => fired = true);
            scheduler.ScheduleOnce(0f, scheduler.Dispose);

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.False);
            Assert.DoesNotThrow(scheduler.Tick);
            Assert.Throws<ObjectDisposedException>(() => scheduler.ScheduleOnce(0f, () => { }));
        }

        [Test]
        public void Schedule_RejectsNonFiniteTimes_AndTickRejectsNonFiniteDelta()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);

            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleOnce(float.NaN, () => { }));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleRepeating(float.PositiveInfinity, () => { }));
            time.UnscaledDeltaTime = float.NaN;
            Assert.Throws<InvalidOperationException>(scheduler.Tick);
        }

        [Test]
        public void ScheduleFixedRate_KeepsTheRate_InsteadOfRoundingUpToAFrame()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.0166f };
            var scheduler = new Scheduler(time);
            var rateFired = 0;
            var delayFired = 0;
            scheduler.ScheduleFixedRateRepeating(0.02f, () => rateFired++, maxCallbacksPerTick: 1);
            scheduler.ScheduleRepeating(0.02f, () => delayFired++);

            for (var tick = 0; tick < 100; tick++)
                scheduler.Tick();

            Assert.That(rateFired, Is.EqualTo(83), "a sub-frame interval is a rate, not one fire per frame");
            Assert.That(delayFired, Is.EqualTo(50), "the default stays fixed-delay and still never bursts");
        }

        [Test]
        public void FixedRate_Bounded_DropsTheExcessAndResumesOnTheNextBoundary()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.1f, () => fired++, maxCallbacksPerTick: 1, FixedRateBacklogPolicy.Bounded);

            time.UnscaledDeltaTime = 0.25f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1), "the per-tick cap bounds one frame");

            time.UnscaledDeltaTime = 0.04f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1), "the dropped callbacks are not replayed");

            time.UnscaledDeltaTime = 0.02f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(2), "the timer resumes 0.05 s after the hitch, keeping its phase");
        }

        [Test]
        public void FixedRate_Preserve_RunsTheDebtOnLaterFrames()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.1f, () => fired++, maxCallbacksPerTick: 1, FixedRateBacklogPolicy.Preserve);

            time.UnscaledDeltaTime = 0.25f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));

            time.UnscaledDeltaTime = 0.01f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(2), "the missed callback is kept as debt");

            time.UnscaledDeltaTime = 0.05f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(3), "the debt is worked off on following frames");
        }

        [Test]
        public void FixedRate_StaysWithinMaxCallbacksPerTick_AfterALongHitch()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 10f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.1f, () => fired++, maxCallbacksPerTick: 3, FixedRateBacklogPolicy.Preserve);

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(3), "one frame runs at most the configured cap");

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(6), "the next frame runs the next capped slice");
        }

        [Test]
        public void FixedRate_CallbackCanDisposeItsOwnHandle_AndNothingElseFiresThisTick()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            IScheduledHandle handle = null;
            handle = scheduler.ScheduleFixedRateRepeating(
                0.1f,
                () =>
                {
                    fired++;
                    handle.Dispose();
                },
                maxCallbacksPerTick: 5,
                FixedRateBacklogPolicy.Preserve);

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));
            Assert.That(handle.IsActive, Is.False);

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));
        }

        [Test]
        public void ReentrantTickFromACallback_IsIgnored()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleRepeating(1f, () =>
            {
                fired++;
                scheduler.Tick();
            });

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(1), "a nested tick must not advance the same frame twice");
        }

        [Test]
        public void SchedulingDuringAFixedRateCallback_DoesNotRunInTheSameTick()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var scheduled = 0;
            var nestedFired = 0;
            scheduler.ScheduleFixedRateRepeating(
                0.1f,
                () =>
                {
                    scheduled++;
                    scheduler.ScheduleOnce(0f, () => nestedFired++);
                },
                maxCallbacksPerTick: 5,
                FixedRateBacklogPolicy.Bounded);

            scheduler.Tick();
            Assert.That(scheduled, Is.EqualTo(5));
            Assert.That(nestedFired, Is.EqualTo(0), "a callback scheduled during a tick starts on the next one");

            scheduler.Tick();
            Assert.That(nestedFired, Is.EqualTo(5));
        }

        [Test]
        public void FixedRate_ThrowingCallback_DoesNotStopTheOthers_AndItsFailuresAreReported()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(
                0.1f,
                () => throw new InvalidOperationException("broken fixed-rate timer"),
                maxCallbacksPerTick: 2,
                FixedRateBacklogPolicy.Bounded);
            scheduler.ScheduleOnce(0.5f, () => fired++);

            Assert.Throws<AggregateException>(() => scheduler.Tick());
            Assert.That(fired, Is.EqualTo(1), "the other timer still ran");
        }

        [Test]
        public void FixedRate_Preserve_DrainsABigBacklogOnFollowingZeroDeltaTicks()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.01f, () => fired++, maxCallbacksPerTick: 2, FixedRateBacklogPolicy.Preserve);

            time.UnscaledDeltaTime = 5f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(2), "one frame never exceeds the cap");

            time.UnscaledDeltaTime = 0f;
            for (var tick = 0; tick < 250; tick++)
                scheduler.Tick();
            Assert.That(fired, Is.EqualTo(500), "the whole 5 s backlog drains at the cap, not only the first frame");

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(500), "a drained timer waits for new time");
        }

        [Test]
        public void FixedRate_Preserve_DrainsDebtOnZeroDeltaTicks()
        {
            var time = new FakeTimeProvider();
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.1f, () => fired++, maxCallbacksPerTick: 1, FixedRateBacklogPolicy.Preserve);

            time.UnscaledDeltaTime = 1f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));

            time.UnscaledDeltaTime = 0f;
            for (var tick = 0; tick < 12; tick++)
                scheduler.Tick();
            Assert.That(fired, Is.EqualTo(9), "the debt drains without new frame time");

            time.UnscaledDeltaTime = 0.1f;
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(10), "normal pacing resumes afterwards");
        }

        [Test]
        public void FixedRate_Preserve_FaultsAndDeactivatesWhenTheIntervalIsBelowTheDebtPrecision()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.1f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            var handle = scheduler.ScheduleFixedRateRepeating(
                float.Epsilon,
                () => fired++,
                maxCallbacksPerTick: 4,
                FixedRateBacklogPolicy.Preserve);

            var error = Assert.Throws<InvalidOperationException>(scheduler.Tick);

            Assert.That(error.Message, Does.Contain("deactivated"));
            Assert.That(error.Message, Does.Contain(nameof(FixedRateBacklogPolicy.Bounded)));
            Assert.That(fired, Is.EqualTo(0), "the fault is raised before any callback runs");
            Assert.That(handle.IsActive, Is.False, "the faulty timer is cancelled explicitly");

            Assert.DoesNotThrow(scheduler.Tick, "a deactivated timer does not fault again");
        }

        [Test]
        public void FixedRate_Preserve_FaultsAndDeactivatesForAnUnrepresentableDebt()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = float.MaxValue };
            var scheduler = new Scheduler(time);
            var fired = 0;
            var handle = scheduler.ScheduleFixedRateRepeating(
                0.001f,
                () => fired++,
                maxCallbacksPerTick: 1,
                FixedRateBacklogPolicy.Preserve);

            var error = Assert.Throws<InvalidOperationException>(scheduler.Tick);

            Assert.That(error.Message, Does.Contain("deactivated"));
            Assert.That(error.Message, Does.Contain(nameof(FixedRateBacklogPolicy.Bounded)));
            Assert.That(fired, Is.EqualTo(0), "the debt is reported before it could be discarded");
            Assert.That(handle.IsActive, Is.False, "the timer is cancelled instead of throwing every frame");

            Assert.DoesNotThrow(scheduler.Tick, "later ticks do not repeat the fault");
        }

        [Test]
        public void A_FixedRateDebtFault_DoesNotSkipTheOtherTimers()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.1f };
            var scheduler = new Scheduler(time);
            var normalFired = 0;
            var faultyFired = 0;
            scheduler.ScheduleOnce(0.05f, () => normalFired++);
            var faulty = scheduler.ScheduleFixedRateRepeating(
                float.Epsilon,
                () => faultyFired++,
                maxCallbacksPerTick: 4,
                FixedRateBacklogPolicy.Preserve);

            var error = Assert.Throws<InvalidOperationException>(scheduler.Tick);

            Assert.That(error.Message, Does.Contain("deactivated"));
            Assert.That(normalFired, Is.EqualTo(1), "the timer after the faulty one still ran");
            Assert.That(faultyFired, Is.EqualTo(0));
            Assert.That(faulty.IsActive, Is.False);

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(normalFired, Is.EqualTo(1), "the other one-shot did not run twice");
        }

        [Test]
        public void A_FixedRateDebtFault_AndACallbackFailure_AreBothInTheAggregate()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.1f };
            var scheduler = new Scheduler(time);
            scheduler.ScheduleOnce(0.05f, () => throw new InvalidOperationException("user callback failed"));
            var faulty = scheduler.ScheduleFixedRateRepeating(
                float.Epsilon,
                () => { },
                maxCallbacksPerTick: 4,
                FixedRateBacklogPolicy.Preserve);

            var error = Assert.Throws<AggregateException>(scheduler.Tick);

            var messages = new System.Text.StringBuilder();
            foreach (var failure in error.Flatten().InnerExceptions)
                messages.AppendLine(failure.Message);

            Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(messages.ToString(), Does.Contain("user callback failed"));
            Assert.That(messages.ToString(), Does.Contain("deactivated"));
            Assert.That(faulty.IsActive, Is.False);

            Assert.DoesNotThrow(scheduler.Tick, "neither failure repeats on the next tick");
        }

        [Test]
        public void FixedRate_Bounded_HugeDeltaDropsTheExcessAndStaysHealthy()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = float.MaxValue };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.001f, () => fired++, maxCallbacksPerTick: 1, FixedRateBacklogPolicy.Bounded);

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(1));

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(2), "bounded dropping keeps the timer usable");
        }

        [Test]
        public void LongDelayAndHugeDeltas_DoNotOverflow()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 1e29f };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleOnce(1e30f, () => fired++);

            for (var tick = 0; tick < 9; tick++)
                scheduler.Tick();
            Assert.That(fired, Is.EqualTo(0));

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1), "a long delay still fires exactly once");
        }

        [Test]
        public void FixedRate_TinyIntervalStaysBoundedByTheCap()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = float.MaxValue };
            var scheduler = new Scheduler(time);
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(float.Epsilon, () => fired++, maxCallbacksPerTick: 2, FixedRateBacklogPolicy.Bounded);

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(2));
        }

        [Test]
        public void FixedRate_RejectsInvalidArguments()
        {
            var scheduler = new Scheduler(new FakeTimeProvider());

            Assert.Throws<ArgumentNullException>(() => scheduler.ScheduleFixedRateRepeating(1f, null, 1, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(float.NaN, () => { }, 1, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(float.PositiveInfinity, () => { }, 1, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(0f, () => { }, 1, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(-1f, () => { }, 1, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(1f, () => { }, 0, FixedRateBacklogPolicy.Bounded));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.ScheduleFixedRateRepeating(1f, () => { }, 1, (FixedRateBacklogPolicy)42));

            scheduler.Dispose();
            Assert.Throws<ObjectDisposedException>(() => scheduler.ScheduleFixedRateRepeating(1f, () => { }, 1, FixedRateBacklogPolicy.Bounded));

            IScheduler noScheduler = null;
            Assert.Throws<ArgumentNullException>(() => noScheduler.ScheduleFixedRateRepeating(1f, () => { }));
        }

        [Test]
        public void SchedulerThatDoesNotSupportFixedRate_ThrowsInsteadOfRunningSlower()
        {
            IScheduler plain = new PlainScheduler();

            Assert.DoesNotThrow(() => plain.ScheduleRepeating(1f, () => { }));
            Assert.Throws<NotSupportedException>(() => plain.ScheduleFixedRateRepeating(1f, () => { }, 1, FixedRateBacklogPolicy.Bounded));

            using var scoped = new ScopedScheduler(plain);
            Assert.Throws<NotSupportedException>(() => scoped.ScheduleFixedRateRepeating(1f, () => { }));
            Assert.Throws<NotSupportedException>(() => ((IScheduler)scoped).ScheduleFixedRateRepeating(1f, () => { }));
        }

        [Test]
        public void ScopedFixedRate_UsesTheApplicationScheduler_AndCancelsWithTheScope()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.35f };
            var scheduler = new Scheduler(time);
            var scope = new ScopedScheduler(scheduler);
            var fired = 0;
            scope.ScheduleFixedRateRepeating(0.1f, () => fired++, 1, FixedRateBacklogPolicy.Bounded);

            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1));

            scope.Dispose();
            scheduler.Tick();
            Assert.That(fired, Is.EqualTo(1), "a disposed scope cancels its fixed-rate timers");
        }

        [Test]
        public void TheExtension_ReachesTheCapabilityThroughTheInterface()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.35f };
            var concrete = new Scheduler(time);
            IScheduler scheduler = concrete;
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(0.1f, () => fired++, 2, FixedRateBacklogPolicy.Bounded);

            concrete.Tick();
            Assert.That(fired, Is.EqualTo(2));
        }

        [Test]
        public void FixedRate_DisposingTheSchedulerFromACallback_StopsTheTick()
        {
            var scheduler = new Scheduler(new FakeTimeProvider { UnscaledDeltaTime = 1f });
            var fired = 0;
            scheduler.ScheduleFixedRateRepeating(
                0.1f,
                () =>
                {
                    fired++;
                    scheduler.Dispose();
                },
                maxCallbacksPerTick: 5,
                FixedRateBacklogPolicy.Preserve);

            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(1));
            Assert.DoesNotThrow(scheduler.Tick);
            Assert.Throws<ObjectDisposedException>(() => scheduler.ScheduleOnce(0f, () => { }));
        }

        [Test]
        public void AThrowingScopedTimer_StillLeavesWithTheScope()
        {
            var time = new FakeTimeProvider { UnscaledDeltaTime = 0.1f };
            var scheduler = new Scheduler(time);
            var scope = new ScopedScheduler(scheduler);
            var fired = 0;
            scope.ScheduleFixedRateRepeating(
                0.1f,
                () =>
                {
                    fired++;
                    throw new InvalidOperationException("broken scoped timer");
                },
                maxCallbacksPerTick: 1,
                FixedRateBacklogPolicy.Bounded);

            Assert.Throws<InvalidOperationException>(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(1));

            scope.Dispose();
            Assert.DoesNotThrow(scheduler.Tick);
            Assert.That(fired, Is.EqualTo(1), "the scope cancels a throwing timer");
        }

        private sealed class PlainScheduler : IScheduler
        {
            public IScheduledHandle ScheduleOnce(float delaySeconds, Action callback) => new Handle();

            public IScheduledHandle ScheduleRepeating(float intervalSeconds, Action callback) => new Handle();

            private sealed class Handle : IScheduledHandle
            {
                public bool IsActive => true;

                public void Dispose()
                {
                }
            }
        }

        private sealed class RecordingMonitor : ITimeIntegrityMonitor
        {
            private readonly Action<float, TimeSpan> _onObserve;
            public RecordingMonitor(Action<float, TimeSpan> onObserve) => _onObserve = onObserve;
            public void Observe(float unscaledDeltaTime, TimeSpan realElapsed) => _onObserve(unscaledDeltaTime, realElapsed);
        }
    }
}
