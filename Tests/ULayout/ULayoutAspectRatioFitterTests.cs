using Foundation.UI.ULayout;
using NUnit.Framework;
using Poke.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation.UI.ULayout.Tests
{
    public sealed class ULayoutAspectRatioFitterTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root)
                Object.DestroyImmediate(_root);
        }

        [TestCase(Layout.LayoutDirection.Row, 2f, 200f, 100f)]
        [TestCase(Layout.LayoutDirection.Column, 0.5f, 100f, 200f)]
        public void LayoutAllocatesSlotAndFitterContainsContent(
            Layout.LayoutDirection direction, float ratio, float width, float height)
        {
            var layout = CreateLayout(direction);
            var fitter = CreateFitter(layout.transform, ratio);
            Rebuild(layout);

            AssertSize(fitter.GetComponent<RectTransform>(), 200f, 200f);
            AssertSize(fitter.Content, width, height);
            Assert.That(fitter.Content.anchoredPosition, Is.EqualTo(new Vector2(100f, 100f)));

            layout.Rect.sizeDelta = new Vector2(100f, 300f);
            Rebuild(layout);

            AssertSize(fitter.GetComponent<RectTransform>(), 100f, 300f);
            Assert.That(fitter.Content.rect.width / fitter.Content.rect.height, Is.EqualTo(ratio).Within(0.001f));
            Assert.That(fitter.Content.rect.width, Is.LessThanOrEqualTo(100f));
            Assert.That(fitter.Content.rect.height, Is.LessThanOrEqualTo(300f));
        }

        [Test]
        public void SiblingAndPaddingAreRespectedWithoutChangingSlotOrRepeatedRebuild()
        {
            var layout = CreateLayout(Layout.LayoutDirection.Row);
            layout.Padding = new Margins { left = 10f, right = 10f, top = 20f, bottom = 20f };
            layout.InnerSpacing = 10f;
            var sibling = new GameObject("Fixed sibling", typeof(RectTransform));
            sibling.transform.SetParent(layout.transform, false);
            ((RectTransform)sibling.transform).sizeDelta = new Vector2(40f, 20f);
            var fitter = CreateFitter(layout.transform, 2f);
            var events = 0;
            layout.OnLayoutChanged += () => events++;
            Rebuild(layout);

            AssertSize(fitter.GetComponent<RectTransform>(), 130f, 160f);
            AssertSize(fitter.Content, 130f, 65f);
            Assert.That(((RectTransform)sibling.transform).rect.width, Is.EqualTo(40f));
            var baseline = events;
            for (var i = 0; i < 3; i++)
            {
                fitter.Refresh();
                Canvas.ForceUpdateCanvases();
            }
            Assert.That(events, Is.EqualTo(baseline), "Fitting must not keep dirtying the parent layout.");
        }

        [Test]
        public void AlignmentUsesContentPivotAndDisabledFitterDoesNotResizeContent()
        {
            var layout = CreateLayout(Layout.LayoutDirection.Row);
            var fitter = CreateFitter(layout.transform, 2f);
            fitter.Content.pivot = new Vector2(0.25f, 0.75f);
            fitter.Alignment = Vector2.one;
            Rebuild(layout);
            Assert.That(fitter.Content.anchoredPosition, Is.EqualTo(new Vector2(50f, 175f)));

            fitter.enabled = false;
            layout.Rect.sizeDelta = new Vector2(400f, 400f);
            Rebuild(layout);
            AssertSize(fitter.Content, 200f, 100f);
            fitter.enabled = true;
            AssertSize(fitter.Content, 400f, 200f);
        }

        [Test]
        public void NestedContentLayoutRebuildsAfterContainerResize()
        {
            var layout = CreateLayout(Layout.LayoutDirection.Row);
            var fitter = CreateFitter(layout.transform, 2f);
            var nested = fitter.Content.gameObject.AddComponent<Layout>();
            SetSizing(nested, SizingMode.Fixed);
            var nestedFitter = CreateFitter(nested.transform, 1f);
            Rebuild(layout);
            AssertSize(nestedFitter.Content, 100f, 100f);

            layout.Rect.sizeDelta = new Vector2(400f, 400f);
            Rebuild(layout);
            AssertSize(nestedFitter.Content, 200f, 200f);
        }

        [Test]
        public void ReparentedSlotUsesNewLayoutAndHandlesZeroSpace()
        {
            var layout = CreateLayout(Layout.LayoutDirection.Row);
            var fitter = CreateFitter(layout.transform, 2f);
            Rebuild(layout);
            var other = new GameObject("Other container", typeof(RectTransform));
            other.transform.SetParent(_root.transform, false);
            ((RectTransform)other.transform).sizeDelta = new Vector2(80f, 160f);
            var otherLayout = other.AddComponent<Layout>();
            SetSizing(otherLayout, SizingMode.Fixed);
            fitter.transform.SetParent(other.transform, false);
            Rebuild(otherLayout);
            AssertSize(fitter.Content, 80f, 40f);

            otherLayout.Rect.sizeDelta = Vector2.zero;
            Rebuild(otherLayout);
            AssertSize(fitter.Content, 0f, 0f);
        }

        [TestCase(1f)]
        [TestCase(2f)]
        public void HeightFromWidthSizesSlotAndPositionsFollowingItemInSameCanvasPass(float ratio)
        {
            var layout = CreateLayout(Layout.LayoutDirection.Column);
            var parentSettings = new SerializedObject(layout);
            parentSettings.FindProperty("m_sizing").FindPropertyRelative("y").enumValueIndex = (int)SizingMode.FitContent;
            parentSettings.ApplyModifiedPropertiesWithoutUndo();
            layout.Padding = new Margins { left = 10f, right = 10f, top = 10f, bottom = 10f };
            layout.InnerSpacing = 10f;
            var fitter = CreateFitter(layout.transform, ratio);
            var item = fitter.GetComponent<LayoutItem>();
            var settings = new SerializedObject(item);
            settings.FindProperty("m_sizing").FindPropertyRelative("y").enumValueIndex = (int)SizingMode.Fixed;
            settings.ApplyModifiedPropertiesWithoutUndo();
            item.Margins = new Margins { left = 8f, right = 12f, top = 5f, bottom = 7f };
            fitter.Mode = ULayoutAspectRatioFitter.FitMode.HeightFromWidth;
            var footer = new GameObject("Following item", typeof(RectTransform));
            footer.transform.SetParent(layout.transform, false);
            var footerRect = (RectTransform)footer.transform;
            footerRect.sizeDelta = new Vector2(100f, 40f);
            Rebuild(layout);

            AssertSize(item.Rect, 160f, 160f / ratio);
            AssertSize(fitter.Content, 160f, 160f / ratio);
            Assert.That(layout.Rect.rect.height, Is.EqualTo(82f + 160f / ratio).Within(0.001f));
            AssertVerticalGap(item.Rect, footerRect, 17f);

            layout.Rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 320f);
            Rebuild(layout);
            AssertSize(item.Rect, 280f, 280f / ratio);
            Assert.That(layout.Rect.rect.height, Is.EqualTo(82f + 280f / ratio).Within(0.001f));
            AssertVerticalGap(item.Rect, footerRect, 17f);

            var events = 0;
            layout.OnLayoutChanged += () => events++;
            for (var i = 0; i < 3; i++)
            {
                fitter.Refresh();
                Canvas.ForceUpdateCanvases();
            }
            Assert.That(events, Is.Zero, "A settled fitter must not keep dirtying the parent layout.");
        }

        [Test]
        public void HeightFromWidthWorksWithoutContentAndReactsToRatioChanges()
        {
            var layout = CreateLayout(Layout.LayoutDirection.Column);
            var fitter = CreateFitter(layout.transform, 1f);
            var settings = new SerializedObject(fitter.GetComponent<LayoutItem>());
            settings.FindProperty("m_sizing").FindPropertyRelative("y").enumValueIndex = (int)SizingMode.Fixed;
            settings.ApplyModifiedPropertiesWithoutUndo();
            fitter.Content = null;
            fitter.Mode = ULayoutAspectRatioFitter.FitMode.HeightFromWidth;
            Rebuild(layout);
            AssertSize(fitter.GetComponent<RectTransform>(), 200f, 200f);
            fitter.AspectRatio = 2f;
            Canvas.ForceUpdateCanvases();
            AssertSize(fitter.GetComponent<RectTransform>(), 200f, 100f);
        }

        private static void AssertVerticalGap(RectTransform upper, RectTransform lower, float expected)
        {
            var upperCorners = new Vector3[4];
            var lowerCorners = new Vector3[4];
            upper.GetWorldCorners(upperCorners);
            lower.GetWorldCorners(lowerCorners);
            Assert.That(upperCorners[0].y - lowerCorners[1].y, Is.EqualTo(expected).Within(0.001f));
        }

        private Layout CreateLayout(Layout.LayoutDirection direction)
        {
            _root = new("Aspect fitter tests", typeof(RectTransform), typeof(Canvas));
            var container = new GameObject("Container", typeof(RectTransform));
            container.transform.SetParent(_root.transform, false);
            var rect = (RectTransform)container.transform;
            rect.sizeDelta = new Vector2(200f, 200f);
            var layout = container.AddComponent<Layout>();
            layout.Direction = direction;
            SetSizing(layout, SizingMode.Fixed);
            return layout;
        }

        private static ULayoutAspectRatioFitter CreateFitter(Transform parent, float ratio)
        {
            var slot = new GameObject("Slot", typeof(RectTransform));
            slot.transform.SetParent(parent, false);
            var item = slot.AddComponent<LayoutItem>();
            SetSizing(item, SizingMode.Grow);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(slot.transform, false);
            var fitter = slot.AddComponent<ULayoutAspectRatioFitter>();
            fitter.Content = (RectTransform)content.transform;
            fitter.AspectRatio = ratio;
            return fitter;
        }

        private static void SetSizing(LayoutItem item, SizingMode sizing)
        {
            var serialized = new SerializedObject(item);
            serialized.FindProperty("m_sizing").FindPropertyRelative("x").enumValueIndex = (int)sizing;
            serialized.FindProperty("m_sizing").FindPropertyRelative("y").enumValueIndex = (int)sizing;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Rebuild(Layout layout)
        {
            layout.QueueCacheRefresh();
            LayoutRebuilder.ForceRebuildLayoutImmediate(layout.Rect);
            Canvas.ForceUpdateCanvases();
        }

        private static void AssertSize(RectTransform rect, float width, float height)
        {
            Assert.That(rect.rect.width, Is.EqualTo(width).Within(0.001f));
            Assert.That(rect.rect.height, Is.EqualTo(height).Within(0.001f));
        }
    }
}
