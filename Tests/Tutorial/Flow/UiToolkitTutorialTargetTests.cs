#if FOUNDATION_UITOOLKIT
using System.Collections;
using Foundation.Tutorial.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Foundation.Tests.Tutorial.Flow
{
    public sealed class UiToolkitTutorialTargetTests
    {
        [UnityTest]
        public IEnumerator Target_FollowsPanelAndVisibilityAndReportsClicks()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("tutorial target panel");
            try
            {
                host.SetActive(false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                host.SetActive(true);

                var registry = new TutorialTargetRegistry();
                var parent = new VisualElement();
                var element = new VisualElement();
                element.style.width = 100;
                element.style.height = 50;
                parent.Add(element);
                using var target = new UiToolkitTutorialTarget(registry, "test.target", element);

                Assert.That(registry.TryGet(target.Id, out _), Is.False);
                document.rootVisualElement.Add(parent);
                yield return null;

                Assert.That(registry.TryGet(target.Id, out var found), Is.True);
                Assert.That(found, Is.SameAs(target));
                Assert.That(target.TryGetWorldBounds(out var bounds), Is.True);
                Assert.That(bounds.size, Is.EqualTo(new Vector2(100, 50)));

                var clicks = 0;
                target.Clicked += () => clicks++;
                using (var click = ClickEvent.GetPooled())
                {
                    click.target = element;
                    element.SendEvent(click);
                }

                Assert.That(clicks, Is.EqualTo(1));
                parent.SetEnabled(false);
                Assert.That(registry.TryGet(target.Id, out _), Is.False);
                target.NotifyClicked();
                Assert.That(clicks, Is.EqualTo(1));
                parent.SetEnabled(true);

                parent.style.display = DisplayStyle.None;
                yield return null;
                Assert.That(registry.TryGet(target.Id, out _), Is.False);
                parent.style.display = DisplayStyle.Flex;
                yield return null;

                parent.RemoveFromHierarchy();
                Assert.That(registry.TryGet(target.Id, out _), Is.False);
                document.rootVisualElement.Add(parent);
                target.SetId("test.rebound");
                Assert.That(registry.TryGet("test.target", out _), Is.False);
                Assert.That(registry.TryGet("test.rebound", out _), Is.True);
                target.Dispose();
                Assert.That(registry.TryGet("test.rebound", out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(settings);
            }
        }
    }
}
#endif
