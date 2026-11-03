using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Foundation.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.Pooling
{
    public sealed class PoolTests
    {
        private static readonly PoolId TestId = PoolId.From("test.pool");

        [Test]
        public void ReturnedInstance_IsReused_AndCountersTrackUse()
        {
            var env = new Env();
            var pool = env.CreatePool();

            var first = pool.Rent();
            pool.Return(first);
            var second = pool.Rent();

            Assert.That(second, Is.SameAs(first));
            Assert.That(env.Strategy.Created, Is.EqualTo(1));
            var stats = pool.Stats;
            Assert.That((stats.Active, stats.Inactive, stats.RentCount, stats.ReturnCount, stats.Misses, stats.Created),
                Is.EqualTo((1, 0, 2, 1, 1, 1)));
        }

        [Test]
        public void EmptyPool_GrowsByTheConfiguredBatch_SoABurstPaysCreationOnce()
        {
            var env = new Env();
            var pool = env.CreatePool(new PoolSettings(growthBatch: 4));

            var rented = new List<TestItem> { pool.Rent(), pool.Rent(), pool.Rent(), pool.Rent() };

            Assert.That(env.Strategy.Created, Is.EqualTo(4));
            Assert.That(pool.Stats.Misses, Is.EqualTo(1), "only the first rent found the pool empty");
            Assert.That(pool.Stats.Expansions, Is.EqualTo(1));
            Assert.That(rented, Is.Unique);
        }

        [Test]
        public void ReturnBeyondMaxRetained_DestroysTheSurplus()
        {
            var env = new Env();
            var pool = env.CreatePool(new PoolSettings(maxRetained: 2));
            var items = new[] { pool.Rent(), pool.Rent(), pool.Rent(), pool.Rent() };

            foreach (var item in items)
                pool.Return(item);

            Assert.That(pool.Stats.Inactive, Is.EqualTo(2));
            Assert.That(env.Strategy.Destroyed, Is.EqualTo(2));
            Assert.That(pool.Stats.Destroyed, Is.EqualTo(2));
        }

        [Test]
        public void Adopt_RespectsMaxRetainedAndDestroysOverflow()
        {
            var env = new Env();
            using var pool = new Pool<TestItem>(
                TestId,
                env.Strategy,
                new PoolSettings(maxRetained: 2),
                clock: env.Clock,
                safetyChecks: false,
                violationHandler: new RecordingHandler(),
                createOnRent: false);
            var items = new[] { new TestItem(), new TestItem(), new TestItem() };

            foreach (var item in items)
                pool.Adopt(item);

            Assert.That(pool.Stats.Created, Is.EqualTo(3));
            Assert.That(pool.Stats.Inactive, Is.EqualTo(2));
            Assert.That(pool.Stats.Destroyed, Is.EqualTo(1));
            Assert.That(env.Strategy.DestroyedItems, Is.EqualTo(new[] { items[2] }));
        }

        [Test]
        public void SafetyChecksDisabled_DuplicateReturnCannotPutAnActiveInstanceInThePoolTwice()
        {
            var env = new Env();
            var violations = new RecordingHandler();
            var pool = env.CreatePool(violations: violations, safetyChecks: false);
            var first = pool.Rent();
            var stillActive = pool.Rent();

            pool.Return(first);
            pool.Return(first);
            var reused = pool.Rent();
            var next = pool.Rent();

            Assert.That(reused, Is.SameAs(first));
            Assert.That(next, Is.Not.SameAs(first));
            Assert.That(pool.Stats.Active, Is.EqualTo(3));
            Assert.That(pool.Stats.Inactive, Is.Zero);
            Assert.That(pool.Stats.ReturnCount, Is.EqualTo(1));
            Assert.That(violations.Kinds, Is.EqualTo(new[] { PoolViolationKind.DoubleReturn }));
            Assert.That(stillActive, Is.Not.SameAs(first));
        }

        [Test]
        public void ValueEqualInstances_AreTrackedByReferenceWhenSafetyChecksAreDisabled()
        {
            var env = new Env();
            var violations = new RecordingHandler();
            var pool = new Pool<TestItem>(
                TestId,
                env.Strategy,
                clock: env.Clock,
                safetyChecks: false,
                violationHandler: violations,
                createOnRent: false);
            pool.Adopt(new TestItem(equalityGroup: 7));
            pool.Adopt(new TestItem(equalityGroup: 7));
            var first = pool.Rent();
            var second = pool.Rent();

            pool.Return(first);
            pool.Return(second);

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(pool.Stats.Active, Is.Zero);
            Assert.That(pool.Stats.Inactive, Is.EqualTo(2));
            Assert.That(pool.Stats.ReturnCount, Is.EqualTo(2));
            Assert.That(violations.Kinds, Is.Empty);
        }

        [Test]
        public void CopiedLease_ReturnsItsItemOnlyOnce()
        {
            var env = new Env();
            var pool = env.CreatePool(safetyChecks: false);
            var firstLease = pool.Lease();
            var secondLease = pool.Lease();
            var copiedLease = firstLease;

            firstLease.Dispose();
            copiedLease.Dispose();
            var reused = pool.Rent();
            var next = pool.Rent();

            Assert.That(reused, Is.SameAs(firstLease.Item));
            Assert.That(next, Is.Not.SameAs(firstLease.Item));
            Assert.That(pool.Stats.ReturnCount, Is.EqualTo(1));
            Assert.That(pool.Stats.Active, Is.EqualTo(3));
            Assert.That(secondLease.Item, Is.Not.SameAs(firstLease.Item));
        }

        [Test]
        public void SafetyChecksDisabled_EqualForeignInstanceIsNotADuplicateReturn()
        {
            var env = new Env();
            var violations = new RecordingHandler();
            var pool = new Pool<TestItem>(TestId, env.Strategy, clock: env.Clock, safetyChecks: false,
                violationHandler: violations, createOnRent: false);
            pool.Adopt(new TestItem(equalityGroup: 7));

            pool.Return(new TestItem(equalityGroup: 7));

            Assert.That(violations.Kinds, Is.EqualTo(new[] { PoolViolationKind.ForeignInstance }));
            Assert.That(pool.Stats.Inactive, Is.EqualTo(1));
        }

        [Test]
        public void MaxActive_RefusesFurtherRents_AccordingToTheConfiguredBehavior()
        {
            var env = new Env();
            var returnsNull = env.CreatePool(new PoolSettings(maxActive: 1));
            var throws = env.CreatePool(new PoolSettings(maxActive: 1, exhaustedBehavior: PoolExhaustedBehavior.Throw), "throws");

            var held = returnsNull.Rent();
            throws.Rent();

            Assert.That(returnsNull.Rent(), Is.Null);
            Assert.That(returnsNull.TryRent(out _), Is.False);
            Assert.That(returnsNull.Stats.Rejected, Is.EqualTo(2));
            Assert.Throws<PoolExhaustedException>(() => throws.Rent());

            returnsNull.Return(held);
            Assert.That(returnsNull.Rent(), Is.SameAs(held), "a returned slot is usable again");
        }

        [Test]
        public void Trim_DestroysTheLongestIdleInstancesFirst()
        {
            var env = new Env();
            var pool = env.CreatePool();
            var a = pool.Rent();
            var b = pool.Rent();
            var c = pool.Rent();
            pool.Return(a);
            pool.Return(b);
            pool.Return(c);

            var removed = pool.Trim(keep: 1);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(env.Strategy.DestroyedItems, Is.EqualTo(new[] { a, b }));
            Assert.That(pool.Rent(), Is.SameAs(c), "the most recently returned instance survives");
        }

        [Test]
        public void IdleTrimming_RemovesOnlyInstancesIdleLongEnough_RespectingMinAndBatch()
        {
            var env = new Env();
            var pool = env.CreatePool(new PoolSettings(minRetained: 1, idleTrimSeconds: 10f, trimBatchSize: 2));
            var items = new List<TestItem>();
            for (var index = 0; index < 5; index++)
                items.Add(pool.Rent());

            env.Clock.Now = 100;
            for (var index = 0; index < 3; index++)
                pool.Return(items[index]);

            env.Clock.Now = 105;
            pool.Return(items[3]);
            pool.Return(items[4]);
            pool.Tick(108); // the first three have been idle 8 s: nothing to trim yet
            Assert.That(pool.Stats.Inactive, Is.EqualTo(5));

            pool.Tick(111); // idle 11 s, but trimming is capped at 2 per check
            Assert.That(pool.Stats.Inactive, Is.EqualTo(3));
            Assert.That(env.Strategy.DestroyedItems, Is.EqualTo(new[] { items[0], items[1] }));

            pool.Tick(111.5); // throttled: the next check is a second later
            Assert.That(pool.Stats.Inactive, Is.EqualTo(3));

            pool.Tick(113); // items[2] is idle long enough; items[3] and items[4] (13 s - 8 s) are not
            Assert.That(pool.Stats.Inactive, Is.EqualTo(2));
            pool.Tick(120); // everything is old now, but MinRetained keeps one
            Assert.That(pool.Stats.Inactive, Is.EqualTo(1));
        }

        [Test]
        public void ReturningTwice_IsDetectedAndIgnored()
        {
            var env = new Env();
            var pool = env.CreatePool();
            var item = pool.Rent();
            pool.Return(item);

            var exception = Assert.Throws<PoolViolationException>(() => pool.Return(item));

            Assert.That(exception.Kind, Is.EqualTo(PoolViolationKind.DoubleReturn));
            Assert.That(pool.Stats.Inactive, Is.EqualTo(1), "the duplicate must not enter the pool a second time");
        }

        [Test]
        public void ReturningToTheWrongPool_NamesTheOwner()
        {
            var env = new Env();
            var owner = env.CreatePool(name: "enemies");
            var other = env.CreatePool(name: "bullets");
            var item = owner.Rent();

            var exception = Assert.Throws<PoolViolationException>(() => other.Return(item));

            Assert.That(exception.Kind, Is.EqualTo(PoolViolationKind.ForeignInstance));
            Assert.That(exception.Message, Does.Contain("enemies"));
            Assert.That(owner.Stats.Active, Is.EqualTo(1), "the owner still considers it rented");
        }

        [Test]
        public void DisposedPool_RejectsRent_DestroysLateReturns_AndReportsLeaks()
        {
            var env = new Env();
            var leaking = env.CreatePool(violations: new RecordingHandler());
            var item = leaking.Rent();
            var idle = leaking.Rent();
            leaking.Return(idle);

            leaking.Dispose();

            Assert.That(((RecordingHandler)env.LastHandler).Kinds, Is.EqualTo(new[] { PoolViolationKind.LeakedOnDispose }));
            Assert.That(env.Strategy.DestroyedItems, Does.Contain(idle), "idle instances are destroyed with the pool");

            leaking.Return(item);
            Assert.That(env.Strategy.DestroyedItems, Does.Contain(item), "an instance returned after disposal is destroyed, not pooled");
        }

        [Test]
        public void Rent_AfterDispose_IsAViolation()
        {
            var env = new Env();
            var pool = env.CreatePool();
            pool.Dispose();

            Assert.Throws<PoolViolationException>(() => pool.Rent());
        }

        [Test]
        public void ThrowingStrategyCallbacks_DestroyOrReleaseEveryAffectedInstance()
        {
            var rentEnv = new Env();
            using var rentPool = rentEnv.CreatePool();
            rentEnv.Strategy.ThrowOnRent = true;
            Assert.Throws<InvalidOperationException>(() => rentPool.Rent());
            Assert.That(rentPool.Stats.Active, Is.Zero);
            Assert.That(rentPool.Stats.Inactive, Is.Zero);
            Assert.That(rentPool.Stats.Destroyed, Is.EqualTo(1));

            var returnEnv = new Env();
            using var returnPool = returnEnv.CreatePool();
            var item = returnPool.Rent();
            returnEnv.Strategy.ThrowOnReturn = true;
            Assert.Throws<InvalidOperationException>(() => returnPool.Return(item));
            Assert.That(returnPool.Stats.Active, Is.Zero);
            Assert.That(returnPool.Stats.Inactive, Is.Zero);
            Assert.That(returnPool.Stats.Destroyed, Is.EqualTo(1));

            var disposeEnv = new Env();
            using var disposePool = disposeEnv.CreatePool();
            disposePool.Prewarm(3);
            disposeEnv.Strategy.ThrowOnDestroy = true;
            Assert.Throws<AggregateException>(disposePool.Dispose);
            Assert.That(disposePool.Stats.Inactive, Is.Zero);
            Assert.That(disposePool.Stats.Destroyed, Is.EqualTo(3));
        }

        [Test]
        public void PrewarmStep_StaysWithinTheBudget_ButAlwaysMakesProgress()
        {
            var env = new Env { CreationCost = 1.0 };
            var pool = env.CreatePool();

            var done = pool.PrewarmStep(10, deadlineSeconds: env.Clock.Now + 2.5);
            Assert.That(done, Is.False);
            Assert.That(pool.Stats.Inactive, Is.EqualTo(3), "created until the 2.5 budget was passed");

            pool.PrewarmStep(10, deadlineSeconds: env.Clock.Now - 100);
            Assert.That(pool.Stats.Inactive, Is.EqualTo(4), "even an exhausted budget creates one instance");

            Assert.That(pool.PrewarmStep(4, deadlineSeconds: 0), Is.True, "already at target: nothing to do");
            Assert.That(pool.Stats.Inactive, Is.EqualTo(4));
        }

        [Test]
        public void Prewarm_NeverExceedsMaxRetained()
        {
            var env = new Env();
            var pool = env.CreatePool(new PoolSettings(maxRetained: 3));

            pool.Prewarm(10);

            Assert.That(pool.Stats.Inactive, Is.EqualTo(3));
            Assert.That(env.Strategy.Created, Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator PrewarmAsync_SpreadsCreationOverFrames_AndReportsProgress()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env { CreationCost = 1.0 };
                var pool = env.CreatePool();
                var reports = new List<float>();

                await pool.PrewarmAsync(9, budgetMillisecondsPerFrame: 3000, new SyncProgress(reports));

                Assert.That(pool.Stats.Inactive, Is.EqualTo(9));
                Assert.That(env.Strategy.Created, Is.EqualTo(9));
                Assert.That(reports, Has.Count.EqualTo(3), "a 3 s budget with 1 s per instance is three instances per frame");
                Assert.That(reports, Is.Ordered);
                Assert.That(reports[reports.Count - 1], Is.EqualTo(1f));
            });
        }

        [UnityTest]
        public IEnumerator PrewarmAsync_StopsWhenCancelled_LeavingWhatWasCreated()
        {
            return UniTask.ToCoroutine(async () =>
            {
                var env = new Env();
                var pool = env.CreatePool();
                using var cancellation = new CancellationTokenSource();

                var task = pool.PrewarmAsync(1000, instancesPerFrame: 2, cancellation.Token);
                await UniTask.Yield();
                cancellation.Cancel();

                try
                {
                    await task;
                    Assert.Fail("The cancelled prewarm should have thrown.");
                }
                catch (OperationCanceledException)
                {
                }

                Assert.That(pool.Stats.Inactive, Is.LessThan(1000));
                Assert.That(pool.Stats.Inactive, Is.GreaterThan(0));
            });
        }

        [Test]
        public void PoolableHooks_RunInLifecycleOrder()
        {
            var log = new List<string>();
            var pool = new Pool<LoggingPoolable>(
                TestId, new PoolableStrategy<LoggingPoolable>(() => new LoggingPoolable(log)),
                safetyChecks: true, violationHandler: ThrowingPoolViolationHandler.Instance);

            var item = pool.Rent();
            pool.Return(item);
            pool.Return(pool.Rent());
            pool.Dispose();

            Assert.That(log, Is.EqualTo(new[] { "created", "rented", "returned", "rented", "returned", "destroying" }));
        }

        [Test]
        public void InstanceDestroyedBehindThePoolsBack_IsSkippedOnRent_AndNeverReturnedToCirculation()
        {
            var env = new Env();
            var pool = env.CreatePool();
            var doomed = pool.Rent();
            var healthy = pool.Rent();
            pool.Return(doomed);
            pool.Return(healthy);
            doomed.Alive = false; // e.g. its scene was unloaded while it sat idle

            var rented = pool.Rent();

            Assert.That(rented, Is.SameAs(healthy));
            Assert.That(pool.Stats.Destroyed, Is.EqualTo(0), "healthy was on top; the dead one is only noticed when reached");
            var next = pool.Rent();
            Assert.That(next, Is.Not.SameAs(doomed));
            Assert.That(env.Strategy.DestroyedItems, Does.Contain(doomed));
        }

        [Test]
        public void ReturningAnInstanceTheEngineAlreadyDestroyed_ReleasesTheSlotWithoutPoolingIt()
        {
            var env = new Env();
            var pool = env.CreatePool();
            var item = pool.Rent();
            item.Alive = false;

            pool.Return(item);

            Assert.That(pool.Stats.Active, Is.EqualTo(0));
            Assert.That(pool.Stats.Inactive, Is.EqualTo(0));
            Assert.That(pool.Stats.Destroyed, Is.EqualTo(1));
        }

        [Test]
        public void InvalidSettings_AreRejectedWithTheReason()
        {
            var exception = Assert.Throws<ArgumentException>(() => new PoolSettings(minRetained: 5, maxRetained: 2));

            Assert.That(exception.Message, Does.Contain("Min retained"));
            Assert.That(new PoolSettings(prewarmCount: 10, maxRetained: 4).Validate(), Is.Not.Empty,
                "a prewarm target above the retention cap is flagged for editors");
        }

        private sealed class SyncProgress : IProgress<float>
        {
            private readonly List<float> _reports;

            public SyncProgress(List<float> reports)
            {
                _reports = reports;
            }

            public void Report(float value) => _reports.Add(value);
        }

        private sealed class TestItem
        {
            private static int _nextEqualityGroup;
            private readonly int _equalityGroup;

            public bool Alive = true;

            public TestItem() : this(Interlocked.Increment(ref _nextEqualityGroup)) { }

            public TestItem(int equalityGroup) => _equalityGroup = equalityGroup;

            public override bool Equals(object other) => other is TestItem item && _equalityGroup == item._equalityGroup;

            public override int GetHashCode() => _equalityGroup;
        }

        private sealed class LoggingPoolable : IPoolable
        {
            private readonly List<string> _log;

            public LoggingPoolable(List<string> log)
            {
                _log = log;
            }

            public void OnCreated() => _log.Add("created");

            public void OnRented() => _log.Add("rented");

            public void OnReturned() => _log.Add("returned");

            public void OnDestroying() => _log.Add("destroying");
        }

        private sealed class RecordingHandler : IPoolViolationHandler
        {
            public readonly List<PoolViolationKind> Kinds = new();

            public void OnViolation(IPool pool, PoolViolationKind kind, string detail) => Kinds.Add(kind);
        }

        private sealed class FakeClock : IPoolClock
        {
            public double Now { get; set; }
        }

        private sealed class CountingStrategy : IPoolStrategy<TestItem>
        {
            private readonly Env _env;

            public CountingStrategy(Env env)
            {
                _env = env;
            }

            public int Created;
            public int Destroyed;
            public readonly List<TestItem> DestroyedItems = new();
            public bool ThrowOnRent;
            public bool ThrowOnReturn;
            public bool ThrowOnDestroy;

            public TestItem Create()
            {
                Created++;
                _env.Clock.Now += _env.CreationCost;
                return new TestItem();
            }

            public bool IsValid(TestItem item) => item.Alive;

            public void OnRent(TestItem item)
            {
                if (ThrowOnRent)
                    throw new InvalidOperationException("rent failed");
            }

            public void OnReturn(TestItem item)
            {
                if (ThrowOnReturn)
                    throw new InvalidOperationException("return failed");
            }

            public void OnDestroy(TestItem item)
            {
                Destroyed++;
                DestroyedItems.Add(item);
                if (ThrowOnDestroy)
                    throw new InvalidOperationException("destroy failed");
            }
        }

        private sealed class Env
        {
            public readonly FakeClock Clock = new();
            public readonly CountingStrategy Strategy;
            public double CreationCost;
            public IPoolViolationHandler LastHandler;

            public Env()
            {
                Strategy = new(this);
            }

            public Pool<TestItem> CreatePool(
                PoolSettings settings = null,
                string name = null,
                IPoolViolationHandler violations = null,
                bool? safetyChecks = true)
            {
                LastHandler = violations ?? ThrowingPoolViolationHandler.Instance;
                return new Pool<TestItem>(TestId, Strategy, settings, name, Clock, safetyChecks, LastHandler);
            }
        }
    }
}
