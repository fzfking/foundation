using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Messaging;
using NUnit.Framework;

namespace Foundation.Tests.Messaging
{
    public sealed class EventBusAwaitTests
    {
        private sealed class SampleEvent
        {
            public int Value;
        }

        [Test]
        public void WaitForEvent_CompletesOnTheFirstPublishedEvent()
        {
            var bus = new EventBus();
            var pending = bus.WaitForEventAsync<SampleEvent>();

            bus.PublishAsync(new SampleEvent { Value = 7 }).GetAwaiter().GetResult();

            Assert.That(pending.GetAwaiter().GetResult().Value, Is.EqualTo(7));
        }

        [Test]
        public void WaitForEvent_IgnoresEventsThatFailThePredicate()
        {
            var bus = new EventBus();
            var pending = bus.WaitForEventAsync<SampleEvent>(sample => sample.Value > 5);

            bus.PublishAsync(new SampleEvent { Value = 1 }).GetAwaiter().GetResult();
            bus.PublishAsync(new SampleEvent { Value = 9 }).GetAwaiter().GetResult();

            Assert.That(pending.GetAwaiter().GetResult().Value, Is.EqualTo(9),
                "the wait must skip non-matching events instead of completing on the first one");
        }

        [Test]
        public void WaitForEvent_WhenPredicateThrows_FaultsTheWaitAndKeepsPublishing()
        {
            var bus = new EventBus();
            var predicateCalls = 0;
            var otherHandlerCalls = 0;
            var error = new InvalidOperationException("predicate failed");
            var pending = bus.WaitForEventAsync<SampleEvent>(_ =>
            {
                predicateCalls++;
                throw error;
            });
            using var otherSubscription = bus.Subscribe(new CountingHandler<SampleEvent>(() => otherHandlerCalls++));

            Assert.DoesNotThrow(() => bus.PublishAsync(new SampleEvent()).GetAwaiter().GetResult());
            var actual = Assert.Throws<InvalidOperationException>(() => pending.GetAwaiter().GetResult());
            bus.PublishAsync(new SampleEvent()).GetAwaiter().GetResult();

            Assert.That(actual, Is.SameAs(error));
            Assert.That(predicateCalls, Is.EqualTo(1));
            Assert.That(otherHandlerCalls, Is.EqualTo(2));
        }

        [Test]
        public void WaitForEvent_IsCancelledWithItsToken()
        {
            var bus = new EventBus();
            using var cancellation = new CancellationTokenSource();
            var pending = bus.WaitForEventAsync<SampleEvent>(cancellationToken: cancellation.Token);

            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
        }

        [Test]
        public void WaitForEvent_ThrowsImmediatelyWhenAlreadyCancelled()
        {
            var bus = new EventBus();
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                bus.WaitForEventAsync<SampleEvent>(cancellationToken: cancellation.Token)
                    .GetAwaiter()
                    .GetResult());
        }

        private sealed class CountingHandler<TEvent> : IEventHandler<TEvent>
        {
            private readonly Action _onHandle;

            public CountingHandler(Action onHandle) => _onHandle = onHandle;

            public UniTask HandleAsync(
                TEvent message,
                CancellationToken cancellationToken = default)
            {
                _onHandle();
                return UniTask.CompletedTask;
            }
        }
    }
}
