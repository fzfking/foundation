using Foundation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.UI
{
    public sealed class TooltipPlacementTests
    {
        private const float Gap = 20f;
        private const float ArrowMargin = 40f;
        private static readonly Rect Screen = new(0f, 0f, 1080f, 1920f);
        private static readonly Vector2 Tooltip = new(900f, 500f);

        [TestCase(200f, TooltipSide.Above, TestName = "Placement_TargetNearBottom_TooltipGoesAbove")]
        [TestCase(1600f, TooltipSide.Below, TestName = "Placement_TargetNearTop_TooltipGoesBelow")]
        public void Placement_PicksRoomierVerticalSide_WithoutCoveringTarget(float targetY, TooltipSide expected)
        {
            var target = new Rect(440f, targetY, 200f, 120f);

            var layout = TooltipPlacement.Compute(target, Tooltip, Screen, Gap, ArrowMargin);

            Assert.That(layout.Side, Is.EqualTo(expected));
            Assert.That(layout.Fits, Is.True);
            Assert.That(TooltipRect(layout).Overlaps(target), Is.False);
            AssertInside(TooltipRect(layout), Screen);
        }

        [Test]
        public void Placement_PreferredSideThatFits_IsHonoured()
        {
            var target = new Rect(440f, 200f, 200f, 120f);

            var layout = TooltipPlacement.Compute(target, Tooltip, Screen, Gap, ArrowMargin, TooltipSide.Below);

            Assert.That(layout.Side, Is.EqualTo(TooltipSide.Above), "Below has only 200px; the preference must yield.");

            layout = TooltipPlacement.Compute(new Rect(440f, 900f, 200f, 120f), Tooltip, Screen, Gap, ArrowMargin, TooltipSide.Below);
            Assert.That(layout.Side, Is.EqualTo(TooltipSide.Below));
        }

        [Test]
        public void Placement_TargetAtScreenEdge_ClampsTooltip_AndArrowStillPointsAtTarget()
        {
            var target = new Rect(960f, 300f, 100f, 100f);

            var layout = TooltipPlacement.Compute(target, Tooltip, Screen, Gap, ArrowMargin);
            var tooltip = TooltipRect(layout);

            AssertInside(tooltip, Screen);
            var arrowX = layout.Center.x + layout.ArrowOffset;
            Assert.That(arrowX, Is.GreaterThan(layout.Center.x), "The arrow must lean towards the target on the right.");
            Assert.That(arrowX, Is.LessThanOrEqualTo(tooltip.xMax - ArrowMargin + 0.01f), "The arrow must stay off the rounded corner.");
        }

        [Test]
        public void Placement_TallTargetWithNoRoomAboveOrBelow_UsesASide()
        {
            var target = new Rect(40f, 100f, 100f, 1700f);
            var size = new Vector2(700f, 500f);

            var layout = TooltipPlacement.Compute(target, size, Screen, Gap, ArrowMargin);

            Assert.That(layout.Side, Is.EqualTo(TooltipSide.Right));
            Assert.That(TooltipRect(layout, size).Overlaps(target), Is.False);
        }

        [Test]
        public void Placement_NothingFits_ReportsIt()
        {
            var target = new Rect(40f, 100f, 1000f, 1700f);

            var layout = TooltipPlacement.Compute(target, Tooltip, Screen, Gap, ArrowMargin);

            Assert.That(layout.Fits, Is.False);
            AssertInside(TooltipRect(layout), Screen);
        }


        private static Rect TooltipRect(TooltipLayout layout) => TooltipRect(layout, Tooltip);

        private static Rect TooltipRect(TooltipLayout layout, Vector2 size)
            => new(layout.Center - size * 0.5f, size);

        private static void AssertInside(Rect rect, Rect bounds)
        {
            Assert.That(rect.xMin, Is.GreaterThanOrEqualTo(bounds.xMin - 0.01f));
            Assert.That(rect.xMax, Is.LessThanOrEqualTo(bounds.xMax + 0.01f));
            Assert.That(rect.yMin, Is.GreaterThanOrEqualTo(bounds.yMin - 0.01f));
            Assert.That(rect.yMax, Is.LessThanOrEqualTo(bounds.yMax + 0.01f));
        }
    }
}
