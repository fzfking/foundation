using System.Collections.Generic;
using Foundation.Pooling;
using NUnit.Framework;

namespace Foundation.Tests.Pooling
{
    public sealed class KeyedViewSetTests
    {
        private static readonly PoolId TestId = PoolId.From("test.keyed-views");

        [Test]
        public void KeptIdsKeepTheirView_AndMissingIdsReturnTheirsToThePool()
        {
            var env = new Env();
            var set = env.CreateSet();

            Sync(set, 1, 2, 3);
            var first = Get(set, 1);
            var second = Get(set, 2);
            Sync(set, 1, 3);

            Assert.That(Get(set, 1), Is.SameAs(first), "a surviving id keeps its view across passes");
            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(env.Released, Is.EqualTo(new[] { second }), "only the vanished id's view was released");
            Assert.That(env.Pool.Stats.Active, Is.EqualTo(2));
        }

        [Test]
        public void ViewOfAVanishedId_IsReusedForANewId_WithoutCreatingAnother()
        {
            var env = new Env();
            var set = env.CreateSet();
            Sync(set, 1);
            var original = Get(set, 1);

            Sync(set);
            Sync(set, 2);

            Assert.That(Get(set, 2), Is.SameAs(original));
            Assert.That(env.Created, Is.EqualTo(1));
        }

        [Test]
        public void Acquire_ReportsCreationOnlyOnFirstSight()
        {
            var env = new Env();
            var set = env.CreateSet();

            set.BeginSync();
            set.Acquire(7, out var firstTime);
            set.EndSync();
            set.BeginSync();
            set.Acquire(7, out var secondTime);
            set.EndSync();

            Assert.That((firstTime, secondTime), Is.EqualTo((true, false)));
        }

        [Test]
        public void Clear_ReturnsEverything_AndTheSetIsReusable()
        {
            var env = new Env();
            var set = env.CreateSet();
            Sync(set, 1, 2, 3);

            set.Clear();

            Assert.That(set.Count, Is.Zero);
            Assert.That(env.Released.Count, Is.EqualTo(3));
            Assert.That(env.Pool.Stats.Active, Is.Zero);
            Sync(set, 5);
            Assert.That(set.Count, Is.EqualTo(1));
        }

        [Test]
        public void ExhaustedPool_ShowsNothingForTheExtraId_AndDoesNotTrackIt()
        {
            var env = new Env(new PoolSettings(maxActive: 1));
            var set = env.CreateSet();

            set.BeginSync();
            var shown = set.Acquire(1, out _);
            var dropped = set.Acquire(2, out var created);
            set.EndSync();

            Assert.That(shown, Is.Not.Null);
            Assert.That(dropped, Is.Null);
            Assert.That(created, Is.False);
            Assert.That(set.Count, Is.EqualTo(1));
        }

        [Test]
        public void RetiredViews_StayWithTheCaller_UntilTheyAreRecycled()
        {
            var env = new Env();
            var set = env.CreateSet();
            Sync(set, 1, 2);
            var leaving = Get(set, 2);
            var retired = new List<View>();

            set.BeginSync();
            set.Acquire(1, out _);
            set.EndSync(retired);

            Assert.That(retired, Is.EqualTo(new[] { leaving }));
            Assert.That(set.Count, Is.EqualTo(1), "the id is no longer tracked");
            Assert.That(env.Released, Is.Empty, "nothing is returned while the leaving animation runs");
            Assert.That(env.Pool.Stats.Active, Is.EqualTo(2));

            set.Recycle(leaving);

            Assert.That(env.Released, Is.EqualTo(new[] { leaving }));
            Assert.That(env.Pool.Stats.Active, Is.EqualTo(1));
        }

        private static void Sync(KeyedViewSet<int, View> set, params int[] ids)
        {
            set.BeginSync();
            foreach (var id in ids)
                set.Acquire(id, out _);
            set.EndSync();
        }

        private static View Get(KeyedViewSet<int, View> set, int id)
        {
            Assert.That(set.TryGet(id, out var view), Is.True);
            return view;
        }

        private sealed class View
        {
        }

        private sealed class Env
        {
            public readonly List<View> Released = new();
            public readonly Pool<View> Pool;
            public int Created;

            public Env(PoolSettings settings = null)
            {
                Pool = new(TestId, new DelegatePoolStrategy<View>(() =>
                {
                    Created++;
                    return new View();
                }), settings, safetyChecks: true, violationHandler: ThrowingPoolViolationHandler.Instance);
            }

            public KeyedViewSet<int, View> CreateSet() => new KeyedViewSet<int, View>(Pool, Released.Add);
        }
    }
}
