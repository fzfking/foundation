using Foundation.UI.UGui;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.UI
{
    public sealed class ScaleContributionTests
    {
        private GameObject _target;

        [SetUp]
        public void CreateTarget() => _target = new GameObject("button");

        [TearDown]
        public void DestroyTarget() => Object.DestroyImmediate(_target);

        // Regression: a press punch read its own half-shrunk scale as the button's normal size, so a button pressed
        // repeatedly while punching shrank for good.
        [Test]
        public void OverlappingPunches_ReleasedInAnyOrder_ReturnToTheOriginalScale()
        {
            _target.transform.localScale = new Vector3(2f, 3f, 1f);
            var first = new ScaleContribution(_target.transform);
            var second = new ScaleContribution(_target.transform);

            first.Set(0.94f);
            second.Set(0.94f);
            first.Release();
            Assert.That(_target.transform.localScale.x, Is.EqualTo(2f * 0.94f).Within(1e-5f), "the second punch is still running");
            second.Release();

            AssertScale(new Vector3(2f, 3f, 1f));
        }

        [Test]
        public void ATabSelectedDuringAPunch_EndsAtTheSelectedSize_NotTheSizeBeforeSelection()
        {
            var state = new ScaleContribution(_target.transform);
            var punch = new ScaleContribution(_target.transform);

            punch.Set(0.94f);
            state.Set(1.2f);
            punch.Release();

            AssertScale(Vector3.one * 1.2f);
        }

        [Test]
        public void ThousandsOfPresses_DoNotDriftTheScale()
        {
            _target.transform.localScale = new Vector3(2f, 3f, 1f);
            for (var press = 0; press < 5000; press++)
            {
                var punch = new ScaleContribution(_target.transform);
                punch.Set(0.92f);
                punch.Set(0.97f);
                punch.Release();
            }

            AssertScale(new Vector3(2f, 3f, 1f));
        }

        [Test]
        public void AScaleSetByTheGameBetweenEffects_IsKept()
        {
            var punch = new ScaleContribution(_target.transform);
            punch.Set(0.9f);
            punch.Release();

            _target.transform.localScale = Vector3.one * 1.5f;
            var next = new ScaleContribution(_target.transform);
            next.Set(0.9f);
            next.Release();

            AssertScale(Vector3.one * 1.5f);
        }

        [Test]
        public void ATargetDestroyedMidEffect_DoesNotFailTheEffectThatEndsAfterIt()
        {
            var punch = new ScaleContribution(_target.transform);
            punch.Set(0.9f);
            Object.DestroyImmediate(_target);

            Assert.DoesNotThrow(punch.Release);
            _target = new GameObject("replacement");
        }

        private void AssertScale(Vector3 expected)
        {
            Assert.That(_target.transform.localScale.x, Is.EqualTo(expected.x).Within(1e-4f));
            Assert.That(_target.transform.localScale.y, Is.EqualTo(expected.y).Within(1e-4f));
            Assert.That(_target.transform.localScale.z, Is.EqualTo(expected.z).Within(1e-4f));
        }
    }
}
