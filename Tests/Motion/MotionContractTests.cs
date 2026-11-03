using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Motion;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Motion
{
    public sealed class MotionContractTests
    {
        // Serialized assets store the number, so the order is part of the contract (it is LitMotion's order).
        [TestCase(MotionEase.Linear, 0)]
        [TestCase(MotionEase.OutQuad, 5)]
        [TestCase(MotionEase.OutCubic, 8)]
        [TestCase(MotionEase.InElastic, 22)]
        [TestCase(MotionEase.InBack, 25)]
        [TestCase(MotionEase.OutBack, 26)]
        [TestCase(MotionEase.InOutBounce, 30)]
        public void TheEaseNumbersStoredInAssets_AreStable(MotionEase ease, int stored)
        {
            Assert.That((int)ease, Is.EqualTo(stored));
        }

        [Test]
        public void VectorTweens_InterpolateEachComponent()
        {
            var seen = new List<Vector2>();
            var service = new ImmediateMotionService();

            service.Animate(new Vector2(0f, 10f), new Vector2(10f, 20f), 1f, MotionEase.Linear, seen.Add);

            Assert.That(seen[0], Is.EqualTo(new Vector2(0f, 10f)));
            Assert.That(seen[seen.Count - 1], Is.EqualTo(new Vector2(10f, 20f)));
        }

        // The default vector tween rides on the float one; the owner's state must reach the callback with every value.
        [Test]
        public void StatefulVectorTween_PassesTheStateToTheCallback()
        {
            var target = new List<Vector3>();
            var service = new ImmediateMotionService();

            service.Animate(Vector3.zero, new Vector3(2f, 4f, 6f), 1f, MotionEase.Linear, target, static (value, list) => list.Add(value));

            Assert.That(target, Is.EqualTo(new[] { Vector3.zero, new Vector3(1f, 2f, 3f), new Vector3(2f, 4f, 6f) }));
        }

        [Test]
        public void ColorAndRotationTweens_EndExactlyOnTheirTarget()
        {
            var colors = new List<Color>();
            var rotations = new List<Quaternion>();
            var service = new ImmediateMotionService();
            var target = Quaternion.Euler(0f, 90f, 0f);

            service.Animate(Color.black, Color.white, 1f, MotionEase.Linear, colors.Add);
            service.Animate(Quaternion.identity, target, 1f, MotionEase.Linear, rotations.Add);

            Assert.That(colors[colors.Count - 1], Is.EqualTo(Color.white));
            Assert.That(Quaternion.Angle(rotations[rotations.Count - 1], target), Is.LessThan(0.01f));
        }

        // Drives the float tween synchronously from 0 to 1 so the typed helpers can be checked without a player loop.
        private sealed class ImmediateMotionService : IMotionService
        {
            public IMotionHandle Animate<TState>(float from, float to, float duration, MotionEase ease, TState state, Action<float, TState> onUpdate, MotionLoop loop = default, CancellationToken lifetime = default)
                where TState : class
            {
                onUpdate(from, state);
                onUpdate(Mathf.Lerp(from, to, 0.5f), state);
                onUpdate(to, state);
                return null;
            }
        }
    }
}
