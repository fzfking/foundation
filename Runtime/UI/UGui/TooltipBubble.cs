using UnityEngine;
using UnityTime = UnityEngine.Time;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// A bubble (the object this sits on) that stands next to a target without covering it and points an arrow at
    /// it: the dialogue box of a tutorial, a hint beside a button. The side comes from <see cref="TooltipPlacement"/>;
    /// the bubble glides to a new placement, follows a target that moves, keeps clear of an optional second area
    /// (a highlight) and stays inside the safe area. With no anchor it stays where it was authored.
    ///
    /// Not driven by itself: the owner calls <see cref="Place"/> once per frame, after whatever it depends on has
    /// been laid out, and gets the bubble's visible extent back to arrange its own neighbours around it.
    /// The arrow sprite must point up; it is rotated onto the bubble's edge that faces the target.
    /// </summary>
    public sealed class TooltipBubble : MonoBehaviour
    {
        // Overlap below this is float noise of the rect maths, not covering.
        private const float OverlapTolerance = 1f;

        [SerializeField] private RectTransform arrow;

        [Tooltip("Gap between the arrow tip (or the bubble edge without an arrow) and the target.")]
        [SerializeField] private float targetGap = 14f;

        [Tooltip("How far the arrow tucks under the bubble edge.")]
        [SerializeField] private float arrowOverlap = 10f;

        [Tooltip("How close to a corner of the bubble the arrow may slide.")]
        [SerializeField] private float arrowCornerMargin = 90f;

        [Tooltip("Space kept between the bubble and the edge of the safe area.")]
        [SerializeField] private float screenMargin = 16f;

        [Tooltip("Exponential follow speed: about 0.25s to settle on a new target at 14, while tracking a moving target without lag.")]
        [SerializeField] private float followSharpness = 14f;

        [Tooltip("Length of the glide to a placement a new anchor asks for.")]
        [SerializeField] private float moveDuration = 0.35f;

        [SerializeField] private float arrowBobPixels = 5f;

        [SerializeField] private float arrowBobPeriod = 1.1f;

        private readonly Vector3[] _corners = new Vector3[4];

        private RectTransform _frame;
        private Vector2 _dockedPosition;
        private Rect _visualLocal;
        private RectTransform _anchor;
        private RectTransform _avoid;
        private TooltipSide _preferredSide;
        private bool _showArrow;
        private bool _snap = true;
        private float _moveStartTime = -1f;
        private Vector2 _moveStartPosition;

        public RectTransform Frame => _frame;

        private float ArrowLength => arrow != null ? arrow.rect.height : 0f;

        private void Awake()
        {
            _frame = (RectTransform)transform;
            _dockedPosition = _frame.anchoredPosition;
            MeasureVisual();
            if (arrow != null)
                arrow.gameObject.SetActive(false);
        }

        /// <summary>Measures the bubble again after its content changed size (a text that was set, a layout that was rebuilt).</summary>
        public void Remeasure()
        {
            if (_frame == null)
                _frame = (RectTransform)transform;
            MeasureVisual();
        }

        /// <summary>Stands next to <paramref name="anchor"/> and points at it; a null anchor docks the bubble back at its authored place.</summary>
        public void SetAnchor(RectTransform anchor, TooltipSide side, bool showArrow)
        {
            if (_anchor != anchor || _preferredSide != side || _showArrow != showArrow)
                BeginMove();
            _anchor = anchor;
            _preferredSide = side;
            _showArrow = showArrow;
        }

        /// <summary>An area the bubble must not cover (a highlighted element); null when there is none. Ignored while inactive.</summary>
        public void SetAvoid(RectTransform avoid) => _avoid = avoid;

        /// <summary>The next <see cref="Place"/> jumps to the placement instead of gliding (a bubble that is just opening).</summary>
        public void Snap() => _snap = true;

        /// <summary>Glides from where the bubble is now to the placement the next frames ask for.</summary>
        public void BeginMove()
        {
            if (_snap || _frame == null || !isActiveAndEnabled)
                return;

            _moveStartTime = UnityTime.unscaledTime;
            _moveStartPosition = _frame.anchoredPosition;
        }

        /// <summary>
        /// Moves the bubble and its arrow for this frame. <paramref name="space"/> is the rect everything is placed in
        /// (the screen panel). Returns the bubble's visible extent in that space.
        /// </summary>
        public Rect Place(RectTransform space, float deltaTime)
        {
            var visual = GetVisualRect(space);
            var targetRect = default(Rect);
            var hasTarget = _anchor != null
                && _anchor.gameObject.activeInHierarchy
                && TryGetLocalRect(_anchor, space, out targetRect);

            var bounds = GetSafeBounds(space);
            var gap = targetGap + (_showArrow ? ArrowLength - arrowOverlap : 0f);
            var side = TooltipSide.Auto;
            var desired = _dockedPosition;
            var arrowTarget = targetRect;
            var pointsAtTarget = hasTarget;
            if (hasTarget)
            {
                var layout = TooltipPlacement.Compute(targetRect, visual.size, bounds, gap, arrowCornerMargin, _preferredSide);
                desired = _frame.anchoredPosition + (layout.Center - visual.center);
                side = layout.Side;
            }

            // The bubble never covers the element it must keep clear: where it would, it moves beside that element
            // instead, and the arrow then points at it from there.
            if (_avoid != null && _avoid.gameObject.activeInHierarchy && TryGetLocalRect(_avoid, space, out var hole))
            {
                var candidate = visual;
                candidate.position += desired - _frame.anchoredPosition;
                if (OverlapArea(candidate, hole) > OverlapTolerance)
                {
                    var layout = TooltipPlacement.Compute(hole, visual.size, bounds, gap, arrowCornerMargin, _preferredSide);
                    desired = _frame.anchoredPosition + (layout.Center - visual.center);
                    side = layout.Side;
                    arrowTarget = hole;
                    pointsAtTarget = true;
                }
            }

            var current = _frame.anchoredPosition;
            Vector2 next;
            if (_snap)
            {
                next = desired;
            }
            else if (TryGetMoveProgress(out var progress))
            {
                next = Vector2.LerpUnclamped(_moveStartPosition, desired, progress);
            }
            else
            {
                next = Vector2.Lerp(current, desired, 1f - Mathf.Exp(-followSharpness * deltaTime));
            }

            _snap = false;
            _frame.anchoredPosition = next;
            visual.position += next - current;

            PlaceArrow(space, visual, _showArrow && pointsAtTarget, side, arrowTarget);
            return visual;
        }

        // Ease-out cubic over the glide's duration; false once it has finished.
        private bool TryGetMoveProgress(out float progress)
        {
            progress = 1f;
            if (_moveStartTime < 0f)
                return false;

            var elapsed = UnityTime.unscaledTime - _moveStartTime;
            if (elapsed >= moveDuration)
            {
                _moveStartTime = -1f;
                return false;
            }

            var remaining = 1f - elapsed / moveDuration;
            progress = 1f - remaining * remaining * remaining;
            return true;
        }

        private void PlaceArrow(RectTransform space, Rect visual, bool visible, TooltipSide side, Rect target)
        {
            if (arrow == null)
                return;

            if (arrow.gameObject.activeSelf != visible)
                arrow.gameObject.SetActive(visible);
            if (!visible)
                return;

            var bob = Mathf.Sin(UnityTime.unscaledTime * (2f * Mathf.PI / arrowBobPeriod)) * arrowBobPixels;
            var halfLength = ArrowLength * 0.5f - arrowOverlap;
            Vector2 position;
            float angle;
            switch (side)
            {
                case TooltipSide.Above:
                    position = new Vector2(ArrowAlong(target.center.x, visual.center.x, visual.width), visual.yMin - halfLength - bob);
                    angle = 180f;
                    break;
                case TooltipSide.Left:
                    position = new Vector2(visual.xMax + halfLength + bob, ArrowAlong(target.center.y, visual.center.y, visual.height));
                    angle = -90f;
                    break;
                case TooltipSide.Right:
                    position = new Vector2(visual.xMin - halfLength - bob, ArrowAlong(target.center.y, visual.center.y, visual.height));
                    angle = 90f;
                    break;
                default:
                    position = new Vector2(ArrowAlong(target.center.x, visual.center.x, visual.width), visual.yMax + halfLength + bob);
                    angle = 0f;
                    break;
            }

            arrow.position = space.TransformPoint(position);
            arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private float ArrowAlong(float targetCenter, float bubbleCenter, float bubbleExtent)
        {
            var limit = Mathf.Max(0f, bubbleExtent * 0.5f - arrowCornerMargin);
            return bubbleCenter + Mathf.Clamp(targetCenter - bubbleCenter, -limit, limit);
        }

        // The visible extent: the frame plus whatever sticks out of it (a header ribbon, a portrait), measured once at
        // rest, before any child animation has moved them. The arrow is placed separately and does not count.
        private void MeasureVisual()
        {
            var min = _frame.rect.min;
            var max = _frame.rect.max;
            for (var index = 0; index < _frame.childCount; index++)
            {
                if (_frame.GetChild(index) is not RectTransform child || !child.gameObject.activeSelf || child == arrow)
                    continue;

                var localPosition = (Vector2)child.localPosition;
                var rect = child.rect;
                min = Vector2.Min(min, localPosition + rect.min);
                max = Vector2.Max(max, localPosition + rect.max);
            }

            _visualLocal = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private Rect GetVisualRect(RectTransform space)
        {
            var origin = (Vector2)space.InverseTransformPoint(_frame.position);
            return new Rect(_visualLocal.position + origin, _visualLocal.size);
        }

        /// <summary>The part of <paramref name="space"/> inside the device safe area, shrunk by the screen margin.</summary>
        public Rect GetSafeBounds(RectTransform space)
        {
            var rect = space.rect;
            var safe = Screen.safeArea;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(space, safe.min, null, out var safeMin)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(space, safe.max, null, out var safeMax))
            {
                rect = Rect.MinMaxRect(
                    Mathf.Max(rect.xMin, safeMin.x),
                    Mathf.Max(rect.yMin, safeMin.y),
                    Mathf.Min(rect.xMax, safeMax.x),
                    Mathf.Min(rect.yMax, safeMax.y));
            }

            return Rect.MinMaxRect(rect.xMin + screenMargin, rect.yMin + screenMargin, rect.xMax - screenMargin, rect.yMax - screenMargin);
        }

        private bool TryGetLocalRect(RectTransform target, RectTransform space, out Rect rect)
        {
            rect = default;
            if (target == null || space == null)
                return false;

            target.GetWorldCorners(_corners);
            var min = (Vector2)space.InverseTransformPoint(_corners[0]);
            var max = (Vector2)space.InverseTransformPoint(_corners[2]);
            rect = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            return true;
        }

        private static float OverlapArea(Rect a, Rect b)
        {
            var width = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            var height = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return width > 0f && height > 0f ? width * height : 0f;
        }
    }
}
