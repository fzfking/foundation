using System;
using System.Collections.Generic;
using Foundation.Reactive;
using Foundation.Tests.Support;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Reactive
{
    public sealed class ReactiveTests
    {
        [Test]
        public void ReactiveValue_DeliversTheCurrentValueOnSubscribe_AndOnlyRealChangesAfter()
        {
            var value = new ReactiveValue<string>("a");
            var seen = new List<string>();

            using var subscription = value.Subscribe(seen.Add);
            value.Value = "a";
            value.Value = "b";

            Assert.That(seen, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(value.Value, Is.EqualTo("b"));
        }

        [Test]
        public void AFailingInitialCallback_IsUnsubscribedBeforeSubscribeThrows()
        {
            var value = new ReactiveValue<int>();
            var calls = 0;

            Assert.Throws<InvalidOperationException>(() => value.Subscribe(_ =>
            {
                calls++;
                throw new InvalidOperationException("initial callback failed");
            }));

            value.Value = 1;

            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void ASubscriberThatFails_IsReported_AndDoesNotKeepOthersFromHearingTheValue()
        {
            var stream = new EventStream<int>();
            var seen = new List<int>();
            stream.Subscribe(_ => throw new InvalidOperationException("broken subscriber"));
            stream.Subscribe(seen.Add);

            using var log = new LogCapture();
            stream.Publish(7);

            Assert.That(log.Contains(LogType.Exception, "broken subscriber"), Is.True, "the failure is reported");
            Assert.That(seen, Is.EqualTo(new[] { 7 }));
        }

        [Test]
        public void UnsubscribingWhilePublishing_DoesNotSkipOrRepeatTheOtherSubscribers()
        {
            var stream = new EventStream<int>();
            var seen = new List<string>();
            IDisposable first = null;
            first = stream.Subscribe(value =>
            {
                seen.Add("first");
                first.Dispose();
            });
            stream.Subscribe(value => seen.Add("second"));

            stream.Publish(1);
            stream.Publish(2);

            Assert.That(seen, Is.EqualTo(new[] { "first", "second", "second" }));
        }

        [Test]
        public void ADisposedStream_IgnoresNewSubscribersAndPublishes()
        {
            var stream = new EventStream<int>();
            var seen = new List<int>();
            stream.Dispose();

            stream.Subscribe(seen.Add).Dispose();
            stream.Publish(1);

            Assert.That(seen, Is.Empty);
        }
    }
}
