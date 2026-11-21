#if FOUNDATION_UITOOLKIT
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Foundation.Tutorial.Flow
{
    /// <summary>Put above the UI in the same panel. Style the four shades with the tutorial-spotlight__shade USS class.</summary>
    public sealed class UiToolkitTutorialSpotlight : VisualElement, ITutorialFocusPresenter
    {
        public const string ShadeClassName = "tutorial-spotlight__shade";

        private static long TrackingIntervalMs { get; } = 0;

        private readonly VisualElement _top;
        private readonly VisualElement _bottom;
        private readonly VisualElement _left;
        private readonly VisualElement _right;
        private readonly IVisualElementScheduledItem _tracking;

        private Vector2 _size;
        private float _padding;
        private Func<Rect, Rect> _expandWorldBounds;

        public event Action<Rect?> HoleChanged;

        public ITutorialTarget Focused { get; private set; }

        /// <summary>The current opening in local coordinates, clipped to the overlay.</summary>
        public Rect? Hole { get; private set; }

        public UiToolkitTutorialSpotlight()
        {
            AddToClassList("tutorial-spotlight");
            pickingMode = PickingMode.Ignore;
            this.StretchToParentSize();
            _top = AddShade();
            _bottom = AddShade();
            _left = AddShade();
            _right = AddShade();

            RegisterCallback<GeometryChangedEvent>(_ => Refresh());
            // Check each panel update because ancestor transforms can move the target without a geometry event.
            _tracking = schedule.Execute(Refresh).Every(TrackingIntervalMs);
            _tracking.Pause();
        }

        public void Focus(TutorialFocusRequest request) => Focus(request, null);

        /// <summary>Lets the caller expand the target bounds in panel coordinates before applying padding.</summary>
        public void Focus(TutorialFocusRequest request, Func<Rect, Rect> expandWorldBounds)
        {
            if (request?.Target is not UiToolkitTutorialTarget target)
            {
                ClearFocus();
                return;
            }

            Focused = target;
            _expandWorldBounds = expandWorldBounds;
            var padding = request.Padding.GetValueOrDefault();
            _padding = float.IsFinite(padding) ? Mathf.Max(0f, padding) : 0f;
            _tracking.Resume();
            Refresh();
        }

        public void ClearFocus()
        {
            _tracking.Pause();
            Focused = null;
            _expandWorldBounds = null;
            Refresh();
        }

        private void Refresh()
        {
            var size = layout.size;
            if (!(size.x > 0f && size.y > 0f))
                size = Vector2.zero;

            var hole = GetHole(size);
            if (_size == size && Hole == hole)
                return;

            var holeChanged = Hole != hole;
            _size = size;
            Hole = hole;
            var opening = hole.GetValueOrDefault();
            SetBounds(_top, 0f, 0f, size.x, opening.yMin);
            SetBounds(_bottom, 0f, opening.yMax, size.x, size.y - opening.yMax);
            SetBounds(_left, 0f, opening.yMin, opening.xMin, opening.height);
            SetBounds(_right, opening.xMax, opening.yMin, size.x - opening.xMax, opening.height);
            if (holeChanged)
                HoleChanged?.Invoke(hole);
        }

        private Rect? GetHole(Vector2 size)
        {
            if (Focused is not UiToolkitTutorialTarget target || panel == null || target.Element.panel != panel ||
                !target.TryGetWorldBounds(out var worldBounds))
                return null;

            var local = this.WorldToLocal(_expandWorldBounds?.Invoke(worldBounds) ?? worldBounds);
            var xMin = Mathf.Max(0f, local.xMin - _padding);
            var yMin = Mathf.Max(0f, local.yMin - _padding);
            var xMax = Mathf.Min(size.x, local.xMax + _padding);
            var yMax = Mathf.Min(size.y, local.yMax + _padding);
            return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : null;
        }

        private VisualElement AddShade()
        {
            var shade = new VisualElement { pickingMode = PickingMode.Position };
            shade.AddToClassList(ShadeClassName);
            shade.style.position = Position.Absolute;
            shade.style.display = DisplayStyle.None;
            Add(shade);
            return shade;
        }

        private static void SetBounds(VisualElement element, float x, float y, float width, float height)
        {
            if (width <= 0f || height <= 0f)
            {
                element.style.display = DisplayStyle.None;
                return;
            }

            element.style.left = x;
            element.style.top = y;
            element.style.width = width;
            element.style.height = height;
            element.style.display = DisplayStyle.Flex;
        }
    }
}
#endif
