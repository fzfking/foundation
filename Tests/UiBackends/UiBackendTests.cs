using System.Collections.Generic;
using Foundation.UI.Imgui;
using Foundation.UI.Toolkit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using System;

namespace Foundation.UI.Backends.Tests
{
    public sealed class UiBackendTests
    {
        private sealed class ToolkitScreen : UiToolkitView
        {
            public bool Bound { get; private set; }

            protected override void OnBound(VisualElement root) => Bound = true;
        }

        private sealed class ImguiScreen : ImguiView
        {
            public List<string> Log;

            public override void Draw(Rect screen) => Log.Add("screen");
        }

        private sealed class ImguiPopup : ImguiView
        {
            public List<string> Log;

            public override void Draw(Rect screen) => Log.Add("popup");
        }

        [Test]
        public void ToolkitViews_AreAddedHiddenToTheirLayer_ShownAndHiddenByTheRouter_AndRemovedOnClose()
        {
            var screens = new VisualElement();
            var popups = new VisualElement();
            var registry = new UIDefinitionRegistry();
            registry.Register<ToolkitScreen>(new UiDefinition(
                "popup",
                new UiToolkitTemplate(() => new Label("hello"), () => new ToolkitScreen()),
                UILayer.Popup,
                UICachePolicy.DestroyOnClose,
                blockInput: true,
                allowBack: true));
            using var router = new UIRouter(registry, new UiToolkitViewFactory(new UiToolkitRoot(screens, popups)));

            var view = router.OpenAsync<ToolkitScreen>().GetAwaiter().GetResult();

            Assert.That(view.Bound, Is.True);
            Assert.That(screens.childCount, Is.EqualTo(0));
            Assert.That(popups.childCount, Is.EqualTo(1));
            Assert.That(popups[0].style.display.value, Is.EqualTo(DisplayStyle.Flex), "the router showed it");

            router.CloseAsync(view).GetAwaiter().GetResult();

            Assert.That(popups.childCount, Is.EqualTo(0));
        }

        [Test]
        public void AToolkitDefinitionWithoutAToolkitTemplate_FailsClearly()
        {
            var registry = new UIDefinitionRegistry();
            registry.Register<ToolkitScreen>(new UiDefinition("x", new AddressViewSource("ui/x"), UILayer.Popup, UICachePolicy.KeepAlive, true, true));
            using var router = new UIRouter(registry, new UiToolkitViewFactory(new UiToolkitRoot(new VisualElement(), new VisualElement())));

            Assert.Throws<InvalidOperationException>(() => router.OpenAsync<ToolkitScreen>().GetAwaiter().GetResult());
        }

        [Test]
        public void ImguiViews_AreDrawnOnlyWhileShown_LowerLayersFirst_AndLeaveTheHostOnClose()
        {
            var log = new List<string>();
            var host = new GameObject("imgui host").AddComponent<ImguiHost>();
            try
            {
                var registry = new UIDefinitionRegistry();
                registry.Register<ImguiPopup>(new UiDefinition(
                    "popup", new ImguiTemplate(() => new ImguiPopup { Log = log }), UILayer.Popup, UICachePolicy.DestroyOnClose, true, true));
                registry.Register<ImguiScreen>(new UiDefinition(
                    "screen", new ImguiTemplate(() => new ImguiScreen { Log = log }), UILayer.FullScreen, UICachePolicy.DestroyOnClose, false, true));
                using var router = new UIRouter(registry, new ImguiViewFactory(host));

                var popup = router.OpenAsync<ImguiPopup>().GetAwaiter().GetResult();
                router.OpenAsync<ImguiScreen>().GetAwaiter().GetResult();
                host.DrawAll(new Rect(0, 0, 100, 100));

                Assert.That(log, Is.EqualTo(new[] { "screen", "popup" }), "the full-screen layer is drawn under the popup");

                router.CloseAsync(popup).GetAwaiter().GetResult();
                log.Clear();
                host.DrawAll(new Rect(0, 0, 100, 100));

                Assert.That(log, Is.EqualTo(new[] { "screen" }));
                Assert.That(host.ViewCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host.gameObject);
            }
        }
    }
}
