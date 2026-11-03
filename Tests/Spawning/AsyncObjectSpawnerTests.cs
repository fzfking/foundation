using Foundation.Spawning;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Spawning
{
    public sealed class AsyncObjectSpawnerTests
    {
        // A copy placed under a parent keeps its authored local placement. The world-space placement moved spawned panels
        // and rows off their places, so the placement the spawner hands to Unity is pinned here.
        [Test]
        public void LocalPlacement_AsksUnityForLocalSpaceUnderTheParent()
        {
            var parent = new GameObject("Spawn parent").transform;
            try
            {
                var placement = AsyncObjectSpawner.LocalPlacement(parent);

                Assert.That(placement.parent, Is.SameAs(parent));
                Assert.That(placement.worldSpace, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(parent.gameObject);
            }
        }

        // UUM-120045 workaround: UI copies are made a few per frame, so a burst of rows is spread over frames. The queue
        // decides how many a frame makes, so it must never make more copies than the budget it is given.
        [Test]
        public void UiQueue_MakesNoMoreCopiesThanTheBudgetOfTheCall()
        {
            var queue = new UiCopyQueue();
            var made = 0;
            for (var i = 0; i < 5; i++)
                queue.Enqueue(() => { made++; return true; });

            Assert.That(queue.Process(2), Is.EqualTo(2));
            Assert.That(made, Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(3));
        }

        // A request that makes no copy (cancelled, or its parent is gone) must not use up the frame's budget.
        [Test]
        public void UiQueue_SkippedRequestsDoNotUseUpTheBudget()
        {
            var queue = new UiCopyQueue();
            var made = 0;
            queue.Enqueue(() => false);
            for (var i = 0; i < 3; i++)
                queue.Enqueue(() => { made++; return true; });

            Assert.That(queue.Process(2), Is.EqualTo(2));
            Assert.That(made, Is.EqualTo(2));
            Assert.That(queue.Count, Is.EqualTo(1));
        }
    }
}
