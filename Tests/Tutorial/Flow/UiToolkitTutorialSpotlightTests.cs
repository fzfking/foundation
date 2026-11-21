#if FOUNDATION_UITOOLKIT
using System.Collections;
using Foundation.Tutorial.Flow;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Foundation.Tests.Tutorial.Flow
{
    public sealed class UiToolkitTutorialSpotlightTests
    {
        [UnityTest]
        public IEnumerator Spotlight_RepositionsHoleAndOnlyPicksOutsideIt()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            var host = new GameObject("tutorial spotlight panel");
            try
            {
                host.SetActive(false);
                var document = host.AddComponent<UIDocument>();
                document.panelSettings = settings;
                host.SetActive(true);

                var stage = new VisualElement();
                stage.style.width = 300;
                stage.style.height = 220;
                var parent = new VisualElement();
                stage.Add(parent);
                var element = new VisualElement();
                element.style.position = Position.Absolute;
                element.style.left = 40;
                element.style.top = 30;
                element.style.width = 50;
                element.style.height = 40;
                parent.Add(element);

                var spotlight = new UiToolkitTutorialSpotlight();
                stage.Add(spotlight);
                document.rootVisualElement.Add(stage);
                using var target = new UiToolkitTutorialTarget(new TutorialTargetRegistry(), "target", element);
                spotlight.Focus(new TutorialFocusRequest { Target = target, Padding = 5 });
                yield return null;
                yield return null;

                var panel = stage.panel;
                var originalCenter = element.worldBound.center;
                Assert.That(spotlight.Hole, Is.EqualTo(new Rect(35, 25, 60, 50)));
                Assert.That(panel.Pick(originalCenter), Is.SameAs(element));
                Assert.That(spotlight.Contains(panel.Pick(stage.LocalToWorld(new Vector2(10, 10)))), Is.True);

                parent.style.translate = new Translate(100, 0);
                yield return null;
                yield return null;
                Assert.That(spotlight.Hole, Is.EqualTo(new Rect(135, 25, 60, 50)));
                Assert.That(panel.Pick(element.worldBound.center), Is.SameAs(element));
                Assert.That(spotlight.Contains(panel.Pick(originalCenter)), Is.True);

                element.RemoveFromHierarchy();
                yield return null;
                yield return null;
                Assert.That(spotlight.Hole, Is.Null);
                Assert.That(spotlight.Contains(panel.Pick(originalCenter)), Is.True);
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
