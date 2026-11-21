using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("Foundation.UI.Backends.Tests")]

namespace Foundation.UI.Toolkit
{
    /// <summary>For a screen-overlay panel, pads the element where its bounds extend outside the screen safe area.</summary>
    [UxmlElement]
    public sealed partial class SafeAreaElement : VisualElement
    {
        private static long SafeAreaPollIntervalMs { get; } = 100;

        public SafeAreaElement()
        {
            pickingMode = PickingMode.Ignore;
            style.flexGrow = 1;
            RegisterCallback<GeometryChangedEvent>(_ => Apply());
            // The safe area and ancestor transforms can change without a local layout event.
            schedule.Execute(Apply).Every(SafeAreaPollIntervalMs);
        }

        private void Apply()
        {
            var currentPanel = panel;
            var size = layout.size;
            if (currentPanel == null || !(size.x > 0f && size.y > 0f))
                return;

            var screen = ToTopLeft(Screen.safeArea, Screen.height);
            var panelMin = RuntimePanelUtils.ScreenToPanel(currentPanel, screen.min);
            var panelMax = RuntimePanelUtils.ScreenToPanel(currentPanel, screen.max);
            var safe = this.WorldToLocal(new Rect(panelMin, panelMax - panelMin));
            var padding = CalculatePadding(new Rect(Vector2.zero, size), safe);

            if (style.paddingLeft != padding.x)
                style.paddingLeft = padding.x;
            if (style.paddingTop != padding.y)
                style.paddingTop = padding.y;
            if (style.paddingRight != padding.z)
                style.paddingRight = padding.z;
            if (style.paddingBottom != padding.w)
                style.paddingBottom = padding.w;
        }

        internal static Rect ToTopLeft(Rect safeArea, float screenHeight) =>
            new(safeArea.xMin, screenHeight - safeArea.yMax, safeArea.width, safeArea.height);

        internal static Vector4 CalculatePadding(Rect bounds, Rect safe) =>
            new(
                Mathf.Clamp(safe.xMin - bounds.xMin, 0f, bounds.width),
                Mathf.Clamp(safe.yMin - bounds.yMin, 0f, bounds.height),
                Mathf.Clamp(bounds.xMax - safe.xMax, 0f, bounds.width),
                Mathf.Clamp(bounds.yMax - safe.yMax, 0f, bounds.height));
    }
}
