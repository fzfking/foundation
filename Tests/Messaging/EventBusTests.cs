using System.Collections.Generic;
using System.Threading;
using System;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Messaging
{
    public sealed class EventBusTests
    {
        private sealed class TestEvent
        {
            public int Value;
        }

        [Test]
        public void PublishWithoutSubscribers_DoesNotFail()
        {
            Assert.DoesNotThrowAsync(async () => await new EventBus().PublishAsync(new TestEvent()));
        }

        [Test]
        public void Publish_InvokesAllSubscribers()
        {
            var bus = new EventBus();
            var received = new List<int>();
            using var first = bus.Subscribe(new RecordingHandler(received, 1));
            using var second = bus.Subscribe(new RecordingHandler(received, 2));

            bus.PublishAsync(new TestEvent { Value = 7 }).GetAwaiter().GetResult();

            CollectionAssert.AreEquivalent(new[] { 1, 2 }, received);
        }

        [Test]
        public void AFailingHandler_DoesNotKeepTheOthersFromTheEvent_AndItsFailureIsReported()
        {
            var bus = new EventBus();
            var received = new List<int>();
            using var failing = bus.Subscribe(new ThrowingHandler());
            using var listening = bus.Subscribe(new RecordingHandler(received, 2));

            var exception = Assert.Throws<InvalidOperationException>(
                () => bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult());

            Assert.That(received, Is.EqualTo(new[] { 2 }), "the handler after the failing one still got the event");
            Assert.That(exception.Message, Is.EqualTo("handler failed"));
        }

        [Test]
        public void SequentialPublish_FailureBeforeACancellationAtTheLoopStart_IsReported()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var failure = new InvalidOperationException("failure before cancellation");
            var received = new List<int>();
            using var cancellation = new CancellationTokenSource();
            using var failing = bus.Subscribe(new SyncFailedHandler(failure));
            using var cancelling = bus.Subscribe(new CancellingHandler(cancellation, throwAfterCancel: false));
            using var after = bus.Subscribe(new RecordingHandler(received, 3));
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => bus.PublishAsync(new TestEvent(), cancellation.Token).GetAwaiter().GetResult());
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(received, Is.Empty, "the handler after the cancellation never ran");
            Assert.That(unobserved, Has.Count.EqualTo(1));
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void SequentialPublish_FailureBeforeACancellationRaisedByAHandler_IsReported()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var failure = new InvalidOperationException("failure before cancellation");
            using var cancellation = new CancellationTokenSource();
            using var failing = bus.Subscribe(new SyncFailedHandler(failure));
            using var cancelling = bus.Subscribe(new CancellingHandler(cancellation));
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => bus.PublishAsync(new TestEvent(), cancellation.Token).GetAwaiter().GetResult());
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1));
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void SequentialPublish_AForeignCancellationFailure_IsReportedWithItsCause()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            using var foreignCancellation = new CancellationTokenSource();
            var failure = new OperationCanceledException(foreignCancellation.Token);
            using var cancellation = new CancellationTokenSource();
            using var failing = bus.Subscribe(new SyncFailedHandler(failure));
            using var cancelling = bus.Subscribe(new CancellingHandler(cancellation));
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => bus.PublishAsync(new TestEvent(), cancellation.Token).GetAwaiter().GetResult());
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1));
            var reported = unobserved[0] as AggregateException;
            Assert.That(reported, Is.Not.Null,
                "an OCE classified as a failure is wrapped so UniTaskScheduler does not drop it");
            Assert.That(reported.InnerException, Is.SameAs(failure));
        }

        [Test]
        public void ConcurrentPublish_ReportsEveryFailureAfterAllHandlersRun()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var received = new List<int>();
            var firstFailure = new InvalidOperationException("first failed");
            var secondFailure = new InvalidOperationException("second failed");
            using var first = bus.Subscribe(new FailedHandler(firstFailure));
            using var second = bus.Subscribe(new FailedHandler(secondFailure));
            using var listening = bus.Subscribe(new RecordingHandler(received, 3));

            var failure = Assert.Throws<AggregateException>(
                () => bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult());

            Assert.That(received, Is.EqualTo(new[] { 3 }), "every handler still got the event");
            CollectionAssert.AreEquivalent(new[] { firstFailure, secondFailure }, failure.InnerExceptions);
        }

        [Test]
        public void ConcurrentPublish_AlreadyCancelled_DoesNotStartAnyHandler()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var received = new List<int>();
            using var subscription = bus.Subscribe(new RecordingHandler(received, 1));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => bus.PublishAsync(new TestEvent(), cancellation.Token));

            Assert.That(received, Is.Empty);
        }

        [Test]
        public void ConcurrentPublish_ACancelledPublish_DoesNotStartHandlersThatHaveNotRunYet()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var received = new List<int>();
            using var cancellation = new CancellationTokenSource();
            using var cancelling = bus.Subscribe(new CancellingHandler(cancellation, throwAfterCancel: false));
            using var after = bus.Subscribe(new RecordingHandler(received, 2));

            Assert.Throws<OperationCanceledException>(
                () => bus.PublishAsync(new TestEvent(), cancellation.Token));

            Assert.That(received, Is.Empty, "the handler after the one that cancelled never started");
        }

        [Test]
        public void ConcurrentPublish_Cancelled_DoesNotWaitForAHandlerThatNeverCompletes()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var never = new UniTaskCompletionSource();
            using var subscription = bus.Subscribe(new PendingHandler(never.Task));
            using var cancellation = new CancellationTokenSource();

            var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
            cancellation.Cancel();

            // Bounded on purpose: a regression leaves the publish pending and fails here instead of blocking the
            // editor thread inside GetResult forever.
            Assert.That(publish.Status, Is.Not.EqualTo(UniTaskStatus.Pending));
            Assert.Throws<OperationCanceledException>(() => publish.GetAwaiter().GetResult());

            never.TrySetResult(); // let the abandoned observer finish
        }

        [Test]
        public void ConcurrentPublish_SynchronousFailureBeforeASynchronousCancellation_IsReported()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var failure = new InvalidOperationException("failure before cancellation");
            using var canceling = new CancellationTokenSource();
            using var failing = bus.Subscribe(new SyncFailedHandler(failure));
            using var cancelingHandler = bus.Subscribe(new CancellingHandler(canceling));
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => bus.PublishAsync(new TestEvent(), canceling.Token));
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1), "the failure collected before cancellation is reported once");
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void ConcurrentPublish_FailureBeforeAnAsynchronousCancellation_IsReported()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var failure = new InvalidOperationException("failure before cancellation");
            var pending = new UniTaskCompletionSource();
            using var failing = bus.Subscribe(new FailedHandler(failure));
            using var waiting = bus.Subscribe(new PendingHandler(pending.Task));
            using var cancellation = new CancellationTokenSource();
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => publish.GetAwaiter().GetResult());

                pending.TrySetResult();
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1), "the failure collected before cancellation is reported once");
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void ConcurrentPublish_Cancelled_ObservesAFailureThatArrivesAfterTheReturn()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var late = new UniTaskCompletionSource();
            var failure = new InvalidOperationException("late failure");
            using var subscription = bus.Subscribe(new PendingHandler(late.Task));
            using var cancellation = new CancellationTokenSource();
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => publish.GetAwaiter().GetResult());

                late.TrySetException(failure);
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1));
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void ConcurrentPublish_AHandlerThatCancelsSynchronously_ObservesTheHandlersStartedBeforeIt()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var late = new UniTaskCompletionSource();
            var failure = new InvalidOperationException("late failure");
            using var cancellation = new CancellationTokenSource();
            using var started = bus.Subscribe(new PendingHandler(late.Task));
            using var cancelling = bus.Subscribe(new CancellingHandler(cancellation));
            var unobserved = new List<Exception>();
            void OnUnobserved(Exception exception) => unobserved.Add(exception);

            UniTaskScheduler.UnobservedTaskException += OnUnobserved;
            try
            {
                Assert.Throws<OperationCanceledException>(
                    () => bus.PublishAsync(new TestEvent(), cancellation.Token));

                late.TrySetException(failure);
            }
            finally
            {
                UniTaskScheduler.UnobservedTaskException -= OnUnobserved;
            }

            Assert.That(unobserved, Has.Count.EqualTo(1));
            Assert.That(unobserved[0], Is.SameAs(failure));
        }

        [Test]
        public void ConcurrentPublish_ConsumesEachHandlerTaskOnce()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var first = new CountingSource();
            var second = new CountingSource();
            using var firstSubscription = bus.Subscribe(new CountingSourceHandler(first));
            using var secondSubscription = bus.Subscribe(new CountingSourceHandler(second));
            using var cancellation = new CancellationTokenSource();

            var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
            first.Complete();
            second.Complete();
            publish.GetAwaiter().GetResult();

            Assert.That(first.Consumed, Is.EqualTo(1));
            Assert.That(second.Consumed, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentPublish_Cancelled_ConsumesAStartedTaskOnceWhenItFinishesLate()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var late = new CountingSource();
            using var subscription = bus.Subscribe(new CountingSourceHandler(late));
            using var cancellation = new CancellationTokenSource();

            var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => publish.GetAwaiter().GetResult());

            late.Complete();

            Assert.That(late.Consumed, Is.EqualTo(1));
        }

        [Test]
        public void ConcurrentPublish_HandlersThatFinishOnDifferentThreads_ReportEveryFailureOnce()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            const int handlerCount = 4;
            var pending = new UniTaskCompletionSource[handlerCount];
            var errors = new InvalidOperationException[handlerCount];
            var subscriptions = new List<IDisposable>(handlerCount);
            using var cancellation = new CancellationTokenSource();
            try
            {
                for (var i = 0; i < handlerCount; i++)
                {
                    pending[i] = new UniTaskCompletionSource();
                    errors[i] = new InvalidOperationException("failure " + i);
                    subscriptions.Add(bus.Subscribe(new PendingHandler(pending[i].Task)));
                }

                var publish = bus.PublishAsync(new TestEvent(), cancellation.Token);
                var threads = new List<Thread>(handlerCount);
                for (var i = 0; i < handlerCount; i++)
                {
                    var index = i;
                    var thread = new Thread(() => pending[index].TrySetException(errors[index]));
                    threads.Add(thread);
                    thread.Start();
                }

                foreach (var thread in threads)
                    thread.Join();

                var failure = Assert.Throws<AggregateException>(() => publish.GetAwaiter().GetResult());
                CollectionAssert.AreEquivalent(errors, failure.InnerExceptions);
            }
            finally
            {
                foreach (var subscription in subscriptions)
                    subscription.Dispose();
            }
        }

        [Test]
        public void SubscriptionAddedAfterAPublish_IsIncludedInTheNextPublish()
        {
            var bus = new EventBus();
            var received = new List<int>();
            using var first = bus.Subscribe(new RecordingHandler(received, 1));
            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();

            using var second = bus.Subscribe(new RecordingHandler(received, 2));
            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo(new[] { 1, 1, 2 }));
        }

        [Test]
        public void DisposedSubscription_AfterTheSnapshotWasCached_IsNotInvoked()
        {
            // Concurrent mode does not re-check a registration before starting it, so only invalidation of the cached
            // snapshot keeps the disposed handler out of the second publish.
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var received = new List<int>();
            var first = bus.Subscribe(new RecordingHandler(received, 1));
            using var second = bus.Subscribe(new RecordingHandler(received, 2));
            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();

            first.Dispose();
            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo(new[] { 1, 2, 2 }));
        }

        [Test]
        public void SubscriptionDisposedOnAnotherThread_WhileAPublishAwaits_IsNotInvoked()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var received = new List<int>();
            using var started = new ManualResetEventSlim(false);
            var release = new UniTaskCompletionSource();
            using var first = bus.Subscribe(new DelegatingHandler<TestEvent>((_, _) =>
            {
                received.Add(1);
                started.Set();
                return release.Task;
            }));
            using var second = bus.Subscribe(new RecordingHandler(received, 2));

            var publish = bus.PublishAsync(new TestEvent());
            try
            {
                Assert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True, "the first handler did not start");

                // Join makes the completed unsubscribe visible to this thread before the first handler is released, so
                // the sequential loop's read of the second registration can only miss it if the flag itself is not
                // visible across threads.
                var unsubscribe = new Thread(second.Dispose);
                unsubscribe.Start();
                Assert.That(unsubscribe.Join(TimeSpan.FromSeconds(5)), Is.True, "unsubscribe did not finish");
            }
            finally
            {
                release.TrySetResult();
            }

            publish.GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { 1 }), "the handler unsubscribed while the first one awaited never runs");
        }

        [Test]
        public void ReentrantSubscribe_DuringAPublish_AffectsOnlyTheNextPublish()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var received = new List<int>();
            IDisposable added = null;
            using var first = bus.Subscribe(new DelegatingHandler<TestEvent>((_, _) =>
            {
                received.Add(1);
                added ??= bus.Subscribe(new RecordingHandler(received, 2));
                return UniTask.CompletedTask;
            }));

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { 1 }), "a handler subscribed during the publish does not get that event");

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            added.Dispose();
            Assert.That(received, Is.EqualTo(new[] { 1, 1, 2 }), "the next publish uses the new subscription");
        }

        [Test]
        public void ReentrantUnsubscribe_DuringAPublish_SkipsTheHandlerAndTheNextPublish()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var received = new List<int>();
            IDisposable target = null;
            using var first = bus.Subscribe(new DelegatingHandler<TestEvent>((_, _) =>
            {
                received.Add(1);
                target.Dispose();
                return UniTask.CompletedTask;
            }));
            using var second = bus.Subscribe(new RecordingHandler(received, 2));
            target = second;

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { 1 }), "the handler after the unsubscribing one is skipped");

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { 1, 1 }), "the unsubscribed handler stays out of the next publish");
        }

        [Test]
        public void NestedPublish_UsesTheSubscriptionsPresentWhenItStarts()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var received = new List<int>();
            IDisposable added = null;
            var nested = false;
            using var first = bus.Subscribe(new DelegatingHandler<TestEvent>((_, _) =>
            {
                received.Add(1);
                if (nested)
                    return UniTask.CompletedTask;

                nested = true;
                added = bus.Subscribe(new RecordingHandler(received, 2));
                bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
                return UniTask.CompletedTask;
            }));

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            added.Dispose();

            Assert.That(received, Is.EqualTo(new[] { 1, 1, 2 }),
                "the nested publish sees the new handler, while the outer publish keeps its snapshot");
        }

        [Test]
        public void PublishDispatchesByStaticInterfaceType_NotTheRuntimeType()
        {
            var bus = new EventBus();
            var received = new List<int>();
            using var first = bus.Subscribe(new DelegatingHandler<IStaticEvent>((_, _) =>
            {
                received.Add(1);
                return UniTask.CompletedTask;
            }));
            using var second = bus.Subscribe(new DelegatingHandler<IStaticEvent>((_, _) =>
            {
                received.Add(2);
                return UniTask.CompletedTask;
            }));

            IStaticEvent message = new StaticDerived();
            bus.PublishAsync(message).GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void PublishDispatchesByStaticBaseType_NotTheRuntimeType()
        {
            var bus = new EventBus();
            var received = new List<int>();
            using var subscription = bus.Subscribe(new DelegatingHandler<StaticBase>((_, _) =>
            {
                received.Add(1);
                return UniTask.CompletedTask;
            }));

            StaticBase message = new StaticDerived();
            bus.PublishAsync(message).GetAwaiter().GetResult();

            Assert.That(received, Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void ConcurrentSubscriptionsAndPublishes_KeepTheCacheConsistent()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var failures = new List<Exception>();
            const int operations = 500;
            using var start = new ManualResetEventSlim(false);

            void RecordFailure(Exception exception)
            {
                lock (failures)
                    failures.Add(exception);
            }

            var churning = new Thread(() =>
            {
                start.Wait();
                try
                {
                    for (var i = 0; i < operations; i++)
                    {
                        var subscription = bus.Subscribe(new DelegatingHandler<TestEvent>((_, _) => UniTask.CompletedTask));
                        subscription.Dispose();
                    }
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }
            });

            var publishing = new Thread(() =>
            {
                start.Wait();
                try
                {
                    for (var i = 0; i < operations; i++)
                        bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    RecordFailure(exception);
                }
            });

            churning.Start();
            publishing.Start();
            start.Set();
            churning.Join();
            publishing.Join();

            Assert.That(failures, Is.Empty);

            var received = new List<int>();
            using var stable = bus.Subscribe(new RecordingHandler(received, 1));
            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
            Assert.That(received, Is.EqualTo(new[] { 1 }), "only the stable subscription remains");
        }

        [Test]
        public void DisposedSubscription_DoesNotRetainItsHandler()
        {
            var bus = new EventBus();
            var handler = SubscribeOnWorkerThread(bus, dispose: true);

            CollectGarbageForRetentionProbe(handler);

            Assert.That(handler.IsAlive, Is.False);
            GC.KeepAlive(bus);
        }

        [Test]
        public void UndisposedSubscription_RemainsReachable_AsTheRetentionProbeControl()
        {
            var bus = new EventBus();
            var handler = SubscribeOnWorkerThread(bus, dispose: false);

            CollectGarbageForRetentionProbe(handler);

            Assert.That(handler.IsAlive, Is.True, "the probe must see a real strong reference");
            GC.KeepAlive(bus);
        }

        // The worker thread owns the only strong reference while it runs. Once it exits, Mono's conservative stack
        // scan cannot root the handler from the test thread's stale locals, so a dead probe means no bus retention.
        private static WeakReference SubscribeOnWorkerThread(EventBus bus, bool dispose)
        {
            WeakReference handler = null;
            Exception failure = null;
            var worker = new Thread(() =>
            {
                try
                {
                    var created = new RecordingHandler(new List<int>(), 1);
                    var subscription = bus.Subscribe(created);
                    bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();
                    if (dispose)
                        subscription.Dispose();
                    handler = new WeakReference(created);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            worker.Start();
            if (!worker.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The subscription worker did not finish.");
            if (failure != null)
                throw new AggregateException("The subscription worker failed.", failure);

            return handler;
        }

        // Mono's conservative GC may need more than one cycle for a recently used local; a really retained handler
        // survives every cycle, so this bounded retry removes the false positive without weakening the assertion.
        private static void CollectGarbageForRetentionProbe(WeakReference handler)
        {
            for (var i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                if (!handler.IsAlive)
                    return;
            }
        }

        private interface IStaticEvent
        {
        }

        private class StaticBase : IStaticEvent
        {
        }

        private sealed class StaticDerived : StaticBase
        {
        }

        private sealed class DelegatingHandler<TEvent> : IEventHandler<TEvent>
        {
            private readonly Func<TEvent, CancellationToken, UniTask> _handle;

            public DelegatingHandler(Func<TEvent, CancellationToken, UniTask> handle)
            {
                _handle = handle;
            }

            public UniTask HandleAsync(TEvent message, CancellationToken cancellationToken = default)
                => _handle(message, cancellationToken);
        }

        private sealed class ThrowingHandler : IEventHandler<TestEvent>
        {
            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("handler failed");
        }

        private sealed class SyncFailedHandler : IEventHandler<TestEvent>
        {
            private readonly Exception _failure;

            public SyncFailedHandler(Exception failure) => _failure = failure;

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
                => throw _failure;
        }

        private sealed class FailedHandler : IEventHandler<TestEvent>
        {
            private readonly Exception _failure;

            public FailedHandler(Exception failure) => _failure = failure;

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
                => UniTask.FromException(_failure);
        }

        private sealed class PendingHandler : IEventHandler<TestEvent>
        {
            private readonly UniTask _pending;

            public PendingHandler(UniTask pending) => _pending = pending;

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
                => _pending;
        }

        private sealed class CancellingHandler : IEventHandler<TestEvent>
        {
            private readonly CancellationTokenSource _cancellation;
            private readonly bool _throwAfterCancel;

            public CancellingHandler(CancellationTokenSource cancellation, bool throwAfterCancel = true)
            {
                _cancellation = cancellation;
                _throwAfterCancel = throwAfterCancel;
            }

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
            {
                _cancellation.Cancel();
                if (_throwAfterCancel)
                    throw new OperationCanceledException(_cancellation.Token);
                return UniTask.CompletedTask;
            }
        }

        private sealed class CountingSourceHandler : IEventHandler<TestEvent>
        {
            private readonly CountingSource _source;

            public CountingSourceHandler(CountingSource source) => _source = source;

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
                => _source.Task;
        }

        private sealed class CountingSource : IUniTaskSource
        {
            private readonly UniTaskCompletionSource _completion = new();

            public int Consumed;

            public UniTask Task => new(this, 0);

            public void Complete() => _completion.TrySetResult();

            public UniTaskStatus GetStatus(short token) => _completion.Task.Status;

            public UniTaskStatus UnsafeGetStatus() => _completion.Task.Status;

            public void OnCompleted(Action<object> continuation, object state, short token)
                => _completion.Task.GetAwaiter().SourceOnCompleted(continuation, state);

            public void GetResult(short token)
            {
                Consumed++;
                _completion.Task.GetAwaiter().GetResult();
            }
        }

        [Test]
        public void DisposedSubscription_IsNotInvoked()
        {
            var bus = new EventBus();
            var received = new List<int>();
            var subscription = bus.Subscribe(new RecordingHandler(received, 1));
            subscription.Dispose();

            bus.PublishAsync(new TestEvent()).GetAwaiter().GetResult();

            Assert.That(received, Is.Empty);
        }

        private sealed class RecordingHandler : IEventHandler<TestEvent>
        {
            private readonly List<int> _received;
            private readonly int _id;

            public RecordingHandler(List<int> received, int id)
            {
                _received = received;
                _id = id;
            }

            public UniTask HandleAsync(TestEvent message, CancellationToken cancellationToken = default)
            {
                _received.Add(_id);
                return UniTask.CompletedTask;
            }
        }
    }
}
