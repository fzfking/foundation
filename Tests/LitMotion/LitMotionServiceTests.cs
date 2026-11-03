using System;
using Foundation.Motion;
using LitMotion;
using NUnit.Framework;

namespace Foundation.Infrastructure.LitMotion.Tests
{
    public sealed class LitMotionServiceTests
    {
        // The adapter maps curves by name and silently falls back to Linear for a name LitMotion lacks, so a renamed or
        // added MotionEase would animate with the wrong curve unless this is caught here.
        [Test]
        public void EveryMotionEase_HasALitMotionCurveOfTheSameNameAndNumber()
        {
            foreach (MotionEase ease in Enum.GetValues(typeof(MotionEase)))
            {
                Assert.That(Enum.TryParse(ease.ToString(), out Ease litMotionEase), Is.True, $"LitMotion has no {ease}");
                Assert.That((int)litMotionEase, Is.EqualTo((int)ease), $"{ease} number differs from LitMotion's");
            }
        }
    }
}
