using System;
using System.Collections.Generic;
using Foundation.Reactive;
using NUnit.Framework;

namespace Foundation.Tests.Reactive
{
    public sealed class DisposableGroupTests
    {
        [Test]
        public void Clear_ReleasesNewestFirst_AndTheGroupIsUsableAgain()
        {
            var log = new List<string>();
            var group = new DisposableGroup();
            group.Add(() => log.Add("socket"));
            group.Add(() => log.Add("session built on the socket"));

            group.Clear();
            group.Add(() => log.Add("second round"));
            group.Clear();

            Assert.That(log, Is.EqualTo(new[] { "session built on the socket", "socket", "second round" }));
            Assert.That(group.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnItemThatThrows_DoesNotKeepTheOthersFromBeingReleased_AndItsFailureIsReported()
        {
            var log = new List<string>();
            var group = new DisposableGroup();
            group.Add(() => log.Add("first"));
            group.Add(() => throw new InvalidOperationException("boom"));
            group.Add(() => log.Add("last"));

            var exception = Assert.Throws<InvalidOperationException>(group.Dispose);

            Assert.That(exception.Message, Is.EqualTo("boom"));
            Assert.That(log, Is.EqualTo(new[] { "last", "first" }));
        }

        // A subscription made after the owner is gone (a late async callback) must not leak.
        [Test]
        public void AddingToADisposedGroup_ReleasesTheItemAtOnce()
        {
            var released = 0;
            var group = new DisposableGroup();
            group.Dispose();

            group.Add(() => released++);
            group.Dispose();

            Assert.That(released, Is.EqualTo(1));
            Assert.That(group.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnItemThatAddsAnotherWhileBeingReleased_StillGetsThatOneReleased()
        {
            var log = new List<string>();
            var group = new DisposableGroup();
            group.Add(() =>
            {
                log.Add("outer");
                group.Add(() => log.Add("added during release"));
            });

            group.Dispose();

            Assert.That(log, Is.EqualTo(new[] { "outer", "added during release" }));
        }

        [Test]
        public void Remove_ReleasesTheItemOnce_AndALaterClearDoesNotReleaseItAgain()
        {
            var released = 0;
            var group = new DisposableGroup();
            var item = group.Add(new CountingDisposable(() => released++));

            Assert.That(group.Remove(item), Is.True);
            Assert.That(group.Remove(item), Is.False, "not owned any more");
            group.Dispose();

            Assert.That(released, Is.EqualTo(1));
        }

        [Test]
        public void AddTo_HandsAStreamSubscriptionToTheGroup_SoClearingStopsDelivery()
        {
            var stream = new EventStream<int>();
            var seen = new List<int>();
            var group = new DisposableGroup();
            stream.Subscribe(seen.Add).AddTo(group);

            stream.Publish(1);
            group.Clear();
            stream.Publish(2);

            Assert.That(seen, Is.EqualTo(new[] { 1 }));
        }

        private sealed class CountingDisposable : IDisposable
        {
            private readonly Action _onDispose;

            public CountingDisposable(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose() => _onDispose();
        }
    }
}
