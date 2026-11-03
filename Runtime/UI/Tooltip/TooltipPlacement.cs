using UnityEngine;

namespace Foundation.UI
{
    /// <summary>Which side of its target a tooltip (a bubble with an arrow) sits on; the arrow is on the opposite edge.</summary>
    public enum TooltipSide
    {
        /// <summary>No preference: the side with the most room.</summary>
        Auto,
        Above,
        Below,
        Left,
        Right,
    }

    public readonly struct TooltipLayout
    {
        public Vector2 Center { get; }

        /// <summary>Which side of the target the tooltip sits on; the arrow is on the opposite edge.</summary>
        public TooltipSide Side { get; }

        public float ArrowOffset { get; }

        /// <summary>False when no side had room and the tooltip may overlap the target.</summary>
        public bool Fits { get; }

        public TooltipLayout(Vector2 center, TooltipSide side, float arrowOffset, bool fits)
        {
            Center = center;
            Side = side;
            ArrowOffset = arrowOffset;
            Fits = fits;
        }
    }

    /// <summary>
    /// Places a tooltip next to a target without covering it: the preferred side if it fits, else above/below
    /// (roomier first), else left/right, kept inside <c>bounds</c>. All rects share one coordinate space, y
    /// pointing up.
    /// </summary>
    public static class TooltipPlacement
    {
        public static TooltipLayout Compute(
            Rect target,
            Vector2 tooltipSize,
            Rect bounds,
            float gap,
            float arrowMargin,
            TooltipSide preferred = TooltipSide.Auto)
        {
            var below = target.yMin - bounds.yMin;
            var above = bounds.yMax - target.yMax;
            var left = target.xMin - bounds.xMin;
            var right = bounds.xMax - target.xMax;

            // The roomier side of each axis first. Plain locals rather than arrays: this runs every frame while a
            // tooltip follows its target.
            var vertical0 = above >= below ? TooltipSide.Above : TooltipSide.Below;
            var vertical1 = above >= below ? TooltipSide.Below : TooltipSide.Above;
            var horizontal0 = right >= left ? TooltipSide.Right : TooltipSide.Left;
            var horizontal1 = right >= left ? TooltipSide.Left : TooltipSide.Right;

            if (preferred != TooltipSide.Auto && Fits(preferred))
                return Place(preferred, true);

            if (Fits(vertical0))
                return Place(vertical0, true);
            if (Fits(vertical1))
                return Place(vertical1, true);
            if (Fits(horizontal0))
                return Place(horizontal0, true);
            if (Fits(horizontal1))
                return Place(horizontal1, true);

            // Nothing fits: take the side with the most room relative to what the tooltip needs.
            var best = TooltipSide.Below;
            var bestRatio = float.MinValue;
            Consider(vertical0);
            Consider(vertical1);
            Consider(horizontal0);
            Consider(horizontal1);

            return Place(best, false);

            void Consider(TooltipSide side)
            {
                var ratio = Space(side) / Need(side);
                if (ratio <= bestRatio)
                    return;

                bestRatio = ratio;
                best = side;
            }

            bool Fits(TooltipSide side) => Space(side) >= Need(side);

            float Need(TooltipSide side)
                => (side == TooltipSide.Left || side == TooltipSide.Right ? tooltipSize.x : tooltipSize.y) + gap;

            float Space(TooltipSide side) => side switch
            {
                TooltipSide.Above => above,
                TooltipSide.Below => below,
                TooltipSide.Left => left,
                _ => right,
            };

            TooltipLayout Place(TooltipSide side, bool fits)
            {
                var half = tooltipSize * 0.5f;
                Vector2 center;
                switch (side)
                {
                    case TooltipSide.Above:
                        center = new Vector2(target.center.x, target.yMax + gap + half.y);
                        break;
                    case TooltipSide.Below:
                        center = new Vector2(target.center.x, target.yMin - gap - half.y);
                        break;
                    case TooltipSide.Left:
                        center = new Vector2(target.xMin - gap - half.x, target.center.y);
                        break;
                    default:
                        center = new Vector2(target.xMax + gap + half.x, target.center.y);
                        break;
                }

                center.x = ClampCenter(center.x, bounds.xMin, bounds.xMax, half.x);
                center.y = ClampCenter(center.y, bounds.yMin, bounds.yMax, half.y);

                var verticalSide = side == TooltipSide.Above || side == TooltipSide.Below;
                var arrowOffset = verticalSide
                    ? ClampArrow(target.center.x - center.x, half.x, arrowMargin)
                    : ClampArrow(target.center.y - center.y, half.y, arrowMargin);
                return new TooltipLayout(center, side, arrowOffset, fits);
            }
        }

        private static float ClampCenter(float value, float min, float max, float half)
            => max - min <= half * 2f ? (min + max) * 0.5f : Mathf.Clamp(value, min + half, max - half);

        private static float ClampArrow(float offset, float half, float margin)
        {
            var limit = Mathf.Max(0f, half - margin);
            return Mathf.Clamp(offset, -limit, limit);
        }
    }
}
