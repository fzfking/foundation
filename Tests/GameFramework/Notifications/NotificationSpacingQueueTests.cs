using System;
using Foundation.GameFramework.Notifications;
using NUnit.Framework;

namespace Foundation.GameFramework.Tests.Notifications
{
    public sealed class NotificationSpacingQueueTests
    {
        [Test]
        public void Reserve_FirstRequest_KeepsRequestedTime()
        {
            var queue = new NotificationSpacingQueue(TimeSpan.FromMinutes(30));
            var requested = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

            var reserved = queue.Reserve(requested);

            Assert.That(reserved, Is.EqualTo(requested));
        }

        [Test]
        public void Reserve_TooCloseToAnExistingSlot_PushesForwardByMinimumSpacing()
        {
            var queue = new NotificationSpacingQueue(TimeSpan.FromMinutes(30));
            var first = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            queue.Reserve(first);

            var reserved = queue.Reserve(first.AddMinutes(10));

            Assert.That(reserved, Is.EqualTo(first.AddMinutes(30)));
        }

        [Test]
        public void Reserve_FarEnoughFromExistingSlots_KeepsRequestedTime()
        {
            var queue = new NotificationSpacingQueue(TimeSpan.FromMinutes(30));
            var first = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            queue.Reserve(first);

            var reserved = queue.Reserve(first.AddMinutes(45));

            Assert.That(reserved, Is.EqualTo(first.AddMinutes(45)));
        }

        [Test]
        public void Reserve_ManyRequestsAtTheSameTime_ChainsThemApart()
        {
            var queue = new NotificationSpacingQueue(TimeSpan.FromMinutes(30));
            var requested = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

            var first = queue.Reserve(requested);
            var second = queue.Reserve(requested);
            var third = queue.Reserve(requested);

            Assert.That(first, Is.EqualTo(requested));
            Assert.That(second, Is.EqualTo(requested.AddMinutes(30)));
            Assert.That(third, Is.EqualTo(requested.AddMinutes(60)));
        }

        [Test]
        public void Clear_ForgetsPreviouslyReservedSlots()
        {
            var queue = new NotificationSpacingQueue(TimeSpan.FromMinutes(30));
            var requested = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);
            queue.Reserve(requested);

            queue.Clear();
            var reserved = queue.Reserve(requested);

            Assert.That(reserved, Is.EqualTo(requested));
        }
    }
}
