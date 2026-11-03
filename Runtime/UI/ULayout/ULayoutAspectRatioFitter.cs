using Poke.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation.UI.ULayout
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LayoutItem))]
    [AddComponentMenu("Foundation/UI/uLayout Aspect Ratio Fitter")]
    public sealed class ULayoutAspectRatioFitter : MonoBehaviour, ILayoutElement
    {
        [SerializeField] private FitMode fitMode;
        [SerializeField, Tooltip("Direct child fitted inside this uLayout slot. Use Fixed sizing on the content.")]
        private RectTransform content;
        [SerializeField, Min(0.0001f), Tooltip("Width divided by height.")]
        private float aspectRatio = 1f;
        [SerializeField, Tooltip("0,0 = bottom-left; 0.5,0.5 = center; 1,1 = top-right.")]
        private Vector2 alignment = new(0.5f, 0.5f);

        private RectTransform _rect;
        private RectTransform _trackedContent;
        private Layout _parentLayout;
        private Layout _contentLayout;
        private DrivenRectTransformTracker _tracker;
        private DrivenRectTransformTracker _slotTracker;
        private bool _drivingHeight;
        private bool _applying;

        public FitMode Mode
        {
            get => fitMode;
            set
            {
                fitMode = value;
                Refresh();
            }
        }

        public float minWidth => -1f;
        public float preferredWidth => -1f;
        public float flexibleWidth => -1f;
        public float minHeight => -1f;
        public float preferredHeight => -1f;
        public float flexibleHeight => -1f;
        public int layoutPriority => 0;

        public RectTransform Content
        {
            get => content;
            set
            {
                _tracker.Clear();
                _trackedContent = null;
                content = value;
                Refresh();
            }
        }

        public float AspectRatio
        {
            get => aspectRatio;
            set
            {
                aspectRatio = SanitizeRatio(value);
                Refresh();
            }
        }

        public Vector2 Alignment
        {
            get => alignment;
            set
            {
                alignment = new(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
                Refresh();
            }
        }

        private void OnEnable()
        {
            _rect = GetComponent<RectTransform>();
            Subscribe();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
            _tracker.Clear();
            _trackedContent = null;
            _slotTracker.Clear();
            _drivingHeight = false;
        }

        private void OnTransformParentChanged()
        {
            if (!isActiveAndEnabled)
                return;

            Unsubscribe();
            Subscribe();
            Refresh();
        }

        private void OnRectTransformDimensionsChange()
        {
            Refresh();
        }

        private void OnValidate()
        {
            aspectRatio = SanitizeRatio(aspectRatio);
            alignment = new(Mathf.Clamp01(alignment.x), Mathf.Clamp01(alignment.y));
        }

        private void LateUpdate()
        {
            Refresh();
        }

        public void CalculateLayoutInputHorizontal()
        {
        }

        public void CalculateLayoutInputVertical()
        {
            Refresh();
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled || _applying || !_rect)
                return;

            UpdateSlotHeight();

            if (_trackedContent != content)
            {
                _tracker.Clear();
                _trackedContent = content;
                _contentLayout = content ? content.GetComponent<Layout>() : null;
                if (content && content.parent == transform)
                    _tracker.Add(this, content, DrivenTransformProperties.Anchors
                        | DrivenTransformProperties.SizeDelta | DrivenTransformProperties.AnchoredPosition);
            }

            if (!content || content.parent != transform)
                return;

            var available = _rect.rect.size;
            available.x = Mathf.Max(0f, available.x);
            available.y = Mathf.Max(0f, available.y);
            var ratio = SanitizeRatio(aspectRatio);
            var width = Mathf.Min(available.x, available.y * ratio);
            var size = new Vector2(width, width / ratio);
            var position = new Vector2(
                (available.x - size.x) * alignment.x + size.x * content.pivot.x,
                (available.y - size.y) * alignment.y + size.y * content.pivot.y);

            if (content.anchorMin == Vector2.zero && content.anchorMax == Vector2.zero
                && content.sizeDelta == size && content.anchoredPosition == position)
                return;

            _applying = true;
            try
            {
                content.anchorMin = Vector2.zero;
                content.anchorMax = Vector2.zero;
                content.sizeDelta = size;
                content.anchoredPosition = position;
                if (!_contentLayout)
                    _contentLayout = content.GetComponent<Layout>();
                if (!_contentLayout) 
                    return;
                
                _contentLayout.SetDirty();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            }
            finally
            {
                _applying = false;
            }
        }

        private void UpdateSlotHeight()
        {
            if (fitMode != FitMode.HeightFromWidth)
            {
                if (_drivingHeight)
                {
                    _slotTracker.Clear();
                    _drivingHeight = false;
                }
                return;
            }

            if (!_drivingHeight)
            {
                _slotTracker.Add(this, _rect, DrivenTransformProperties.SizeDeltaY);
                _drivingHeight = true;
            }

            var height = Mathf.Max(0f, _rect.rect.width) / SanitizeRatio(aspectRatio);
            if (Mathf.Approximately(_rect.rect.height, height))
                return;

            _applying = true;
            try
            {
                _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                if (_parentLayout && !CanvasUpdateRegistry.IsRebuildingLayout())
                {
                    _parentLayout.SetDirty();
                    LayoutRebuilder.MarkLayoutForRebuild(_parentLayout.Rect);
                }
            }
            finally
            {
                _applying = false;
            }
        }

        private void Subscribe()
        {
            if (transform.parent)
                _parentLayout = transform.parent.GetComponent<Layout>();
            if (_parentLayout)
                _parentLayout.OnLayoutChanged += Refresh;
        }

        private void Unsubscribe()
        {
            if (_parentLayout)
                _parentLayout.OnLayoutChanged -= Refresh;
            _parentLayout = null;
        }

        private static float SanitizeRatio(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(0.0001f, value);

        public enum FitMode
        {
            ContainContent,
            HeightFromWidth
        }
    }
}
