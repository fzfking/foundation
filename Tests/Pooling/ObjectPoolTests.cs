using Foundation.Pooling;
using NUnit.Framework;
using System;

namespace Foundation.Tests.Pooling
{
    public sealed class ObjectPoolTests
    {
        [Test]
        public void ReturningTheSameObjectTwice_IsRefused_SoItIsNeverHandedToTwoCallers()
        {
            var pool = new ObjectPool<object>(() => new object());
            var item = pool.Rent();
            pool.Return(item);

            Assert.Throws<InvalidOperationException>(() => pool.Return(item));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.CountActive, Is.EqualTo(0));
            Assert.That(pool.Rent(), Is.Not.SameAs(pool.Rent()));
        }

        [Test]
        public void Return_ThenRent_ReusesTheSameInstance()
        {
            var pool = new ObjectPool<object>(() => new object());
            var first = pool.Rent();
            pool.Return(first);

            var second = pool.Rent();

            Assert.That(second, Is.SameAs(first));
            Assert.That(pool.CountInactive, Is.EqualTo(0));
        }

        [Test]
        public void Return_BeyondMaxSize_DestroysInsteadOfPooling()
        {
            var destroyed = 0;
            var pool = new ObjectPool<object>(() => new object(), onDestroy: _ => destroyed++, maxSize: 1);
            var first = pool.Rent();
            var second = pool.Rent();

            pool.Return(first);
            pool.Return(second);

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(destroyed, Is.EqualTo(1));
        }

        [Test]
        public void Clear_DestroysEveryInactiveInstance()
        {
            var destroyed = 0;
            var pool = new ObjectPool<object>(() => new object(), onDestroy: _ => destroyed++);
            var item = pool.Rent();
            pool.Return(item);

            pool.Clear();

            Assert.That(destroyed, Is.EqualTo(1));
            Assert.That(pool.CountInactive, Is.EqualTo(0));
        }
    }
}
