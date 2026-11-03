using System;
using System.Collections;
using System.Threading;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using System.Collections.Generic;

namespace Foundation.Tests.Motion
{
    public sealed class MotionEngineTests
    {
        [Test]
        public void EveryEase_StartsAtZero_AndEndsAtOne()
        {
            foreach (MotionEase ease in Enum.GetValues(typeof(MotionEase)))
            {
                Assert.That(MotionEasing.Evaluate(ease, 0f), Is.EqualTo(0f).Within(1e-5f), $"{ease} at 0");
                Assert.That(MotionEasing.Evaluate(ease, 1f), Is.EqualTo(1f).Within(1e-5f), $"{ease} at 1");
            }
        }

        [TestCase(MotionEase.Linear, 0.5f, 0.5f)]
        [TestCase(MotionEase.InQuad, 0.5f, 0.25f)]
        [TestCase(MotionEase.OutQuad, 0.5f, 0.75f)]
        [TestCase(MotionEase.InOutQuad, 0.25f, 0.125f)]
        [TestCase(MotionEase.InCubic, 0.5f, 0.125f)]
        [TestCase(MotionEase.InOutSine, 0.5f, 0.5f)]
        public void KnownCurves_MatchTheirDefinition(MotionEase ease, float t, float expected)
        {
            Assert.That(MotionEasing.Evaluate(ease, t), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void ProgressOutsideTheUnitRange_IsClamped()
        {
            Assert.That(MotionEasing.Evaluate(MotionEase.InQuad, -3f), Is.EqualTo(0f));
            Assert.That(MotionEasing.Evaluate(MotionEase.InQuad, 4f), Is.EqualTo(1f));
        }

        [Test]
        public void Animate_RejectsNonFiniteDurationBeforeStartingAMotion()
        {
            var service = new PlayerLoopMotionService();
            var updates = 0;
            foreach (var duration in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    service.Animate(0f, 1f, duration, MotionEase.Linear, _ => updates++));

            Assert.That(updates, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ZeroDuration_StillCompletesAtTheTargetValue() => UniTask.ToCoroutine(async () =>
        {
            var values = new List<float>();
            var handle = new PlayerLoopMotionService().Animate(
                2f, 6f, 0f, MotionEase.Linear, values, static (value, list) => list.Add(value));

            await handle.WaitAsync();

            Assert.That(values, Is.EqualTo(new[] { 6f }));
            Assert.That(handle.IsActive, Is.False);
        });

        [UnityTest]
        public IEnumerator AMotion_UpdatesTheStateItWasGiven_AndEndsOnTheTargetValue() => UniTask.ToCoroutine(async () =>
        {
            var values = new List<float>();

            var handle = new PlayerLoopMotionService().Animate(2f, 6f, 0.05f, MotionEase.Linear, values, static (value, list) => list.Add(value));
            await handle.WaitAsync();

            Assert.That(values[values.Count - 1], Is.EqualTo(6f));
            Assert.That(values, Is.Ordered);
        });

        [UnityTest]
        public IEnumerator ACancelledMotion_ThrowsToItsWaiter_AndStopsUpdating() => UniTask.ToCoroutine(async () =>
        {
            var updates = 0;
            var handle = new PlayerLoopMotionService().Animate(0f, 1f, 30f, MotionEase.Linear, _ => updates++);
            await UniTask.Yield();

            handle.Cancel();

            await AssertCancelledAsync(() => handle.WaitAsync());
            var seen = updates;
            await UniTask.Yield();
            await UniTask.Yield();
            Assert.That(updates, Is.EqualTo(seen));
            Assert.That(handle.IsActive, Is.False);
        });

        [UnityTest]
        public IEnumerator ACancelledWaitToken_CancelsTheMotionItself() => UniTask.ToCoroutine(async () =>
        {
            using var source = new CancellationTokenSource();
            var handle = new PlayerLoopMotionService().Animate(0f, 1f, 30f, MotionEase.Linear, _ => { });
            var wait = handle.WaitAsync(source.Token);

            source.Cancel();

            await AssertCancelledAsync(() => wait);
            Assert.That(handle.IsActive, Is.False);
        });

        [UnityTest]
        public IEnumerator AMotion_StopsWhenItsLifetimeEnds() => UniTask.ToCoroutine(async () =>
        {
            using var lifetime = new CancellationTokenSource();
            var handle = new PlayerLoopMotionService().Animate(0f, 1f, 30f, MotionEase.Linear, _ => { }, lifetime: lifetime.Token);
            await UniTask.Yield();

            lifetime.Cancel();
            await UniTask.Yield();

            Assert.That(handle.IsActive, Is.False);
        });

        private static async UniTask AssertCancelledAsync(Func<UniTask> wait)
        {
            try
            {
                await wait();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Assert.Fail("Expected the motion to be cancelled.");
        }
    }
}
