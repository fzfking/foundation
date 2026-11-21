using System.Collections.Generic;
using System.Threading;
using Foundation.UI.Imgui;
using Foundation.UI.Toolkit;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using Object = UnityEngine.Object;

namespace Foundation.UI.Backends.Tests
{
    public sealed class UiBackendTests
    {
        private sealed class ToolkitScreen : UiToolkitView
        {
            public Action<VisualElement> BindAction;
            public bool Bound { get; private set; }
            public int HideCount { get; private set; }
            public int DisposeCount { get; private set; }
            public CancellationToken Token => VisibilityToken;

            protected override void OnBound(VisualElement root)
            {
                Bound = true;
                BindAction?.Invoke(root);
            }

            protected override void OnHide() => HideCount++;

            protected override void OnDispose() => DisposeCount++;
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
            var token = view.Token;

            router.CloseAsync(view).GetAwaiter().GetResult();

            Assert.That(popups.childCount, Is.EqualTo(0));
            Assert.That(token.IsCancellationRequested, Is.True);
            Assert.That(view.HideCount, Is.EqualTo(1));
            Assert.That(view.DisposeCount, Is.EqualTo(1));
        }

        [Test]
        public void ToolkitView_ReopeningStartsAFreshVisibilityLifetime()
        {
            using var view = new ToolkitScreen();
            using var open = new CancellationTokenSource();
            view.Bind(new VisualElement());
            view.ShowAsync(open.Token).GetAwaiter().GetResult();
            var first = view.Token;

            open.Cancel();
            Assert.That(first.IsCancellationRequested, Is.False);
            view.HideAsync().GetAwaiter().GetResult();
            view.HideAsync().GetAwaiter().GetResult();
            Assert.That(view.HideCount, Is.EqualTo(1));
            Assert.That(first.IsCancellationRequested, Is.True);

            view.ShowAsync().GetAwaiter().GetResult();
            var second = view.Token;
            Assert.That(second.IsCancellationRequested, Is.False);
            view.Dispose();

            Assert.That(second.IsCancellationRequested, Is.True);
            Assert.That(view.HideCount, Is.EqualTo(2));
        }

        [Test]
        public void ToolkitViewFactory_FailedBindReleasesViewAndTree()
        {
            var parent = new VisualElement();
            var view = new ToolkitScreen
            {
                BindAction = element =>
                {
                    parent.Add(element);
                    throw new InvalidOperationException("Bind failed.");
                }
            };
            var definition = new UiDefinition("broken",
                new UiToolkitTemplate(() => new VisualElement(), () => view),
                UILayer.Popup, UICachePolicy.DestroyOnClose, false, true);
            var factory = new UiToolkitViewFactory(new UiToolkitRoot(parent, parent));

            Assert.Throws<InvalidOperationException>(() => factory.CreateAsync(definition, default));
            Assert.That(parent.childCount, Is.Zero);
            Assert.That(view.DisposeCount, Is.EqualTo(1));
            Assert.That(view.HideCount, Is.Zero);
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
