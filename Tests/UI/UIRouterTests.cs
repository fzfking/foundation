using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.UI;
using NUnit.Framework;

namespace Foundation.Tests.UI
{
    public sealed class UIRouterTests
    {
        private sealed class TestView : IUIView
        {
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public bool Destroyed { get; set; }

            public UniTask ShowAsync(CancellationToken cancellationToken = default)
            {
                ShowCount++;
                return UniTask.CompletedTask;
            }

            public UniTask HideAsync(CancellationToken cancellationToken = default)
            {
                HideCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class OtherView : IUIView
        {
            public UniTask ShowAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;

            public UniTask HideAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        [Test]
        public void OpenSameView_DoesNotCreateDuplicateByDefault()
        {
            var views = new FakeViewFactory();
            var router = CreateRouter(views, UICachePolicy.KeepAlive, allowBack: true);

            var first = router.OpenAsync<TestView>().GetAwaiter().GetResult();
            var second = router.OpenAsync<TestView>().GetAwaiter().GetResult();

            Assert.That(second, Is.SameAs(first));
            Assert.That(views.Created, Is.EqualTo(1));
            Assert.That(first.ShowCount, Is.EqualTo(2));
        }

        [Test]
        public void KeepAlive_ReusesInstanceAfterClose_AndNeverDestroysIt()
        {
            var views = new FakeViewFactory();
            var router = CreateRouter(views, UICachePolicy.KeepAlive, allowBack: true);

            var first = router.OpenAsync<TestView>().GetAwaiter().GetResult();
            router.CloseAsync(first).GetAwaiter().GetResult();
            var second = router.OpenAsync<TestView>().GetAwaiter().GetResult();

            Assert.That(second, Is.SameAs(first));
            Assert.That(views.Created, Is.EqualTo(1));
            Assert.That(first.Destroyed, Is.False);
        }

        [Test]
        public void DestroyOnClose_DestroysTheClosedViewAndCreatesANewOneOnNextOpen()
        {
            var views = new FakeViewFactory();
            var router = CreateRouter(views, UICachePolicy.DestroyOnClose, allowBack: true);

            var first = router.OpenAsync<TestView>().GetAwaiter().GetResult();
            router.CloseAsync(first).GetAwaiter().GetResult();
            var second = router.OpenAsync<TestView>().GetAwaiter().GetResult();

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.Destroyed, Is.True);
            Assert.That(views.Created, Is.EqualTo(2));
        }

        [Test]
        public void BackWithoutAllowBack_DoesNotClose()
        {
            var router = CreateRouter(new FakeViewFactory(), UICachePolicy.KeepAlive, allowBack: false);
            var view = router.OpenAsync<TestView>().GetAwaiter().GetResult();

            var closed = router.BackAsync().GetAwaiter().GetResult();

            Assert.That(closed, Is.False);
            Assert.That(view.HideCount, Is.EqualTo(0));
        }

        [Test]
        public void ReplaceAsync_ReplacesTheWholeScopeStack()
        {
            var router = CreateRouter(new FakeViewFactory(), UICachePolicy.KeepAlive, allowBack: true);
            var firstDefinition = new UiDefinition("first", new AddressViewSource("ui/first"), UILayer.FullScreen, UICachePolicy.KeepAlive, false, true);
            var secondDefinition = new UiDefinition("second", new AddressViewSource("ui/second"), UILayer.FullScreen, UICachePolicy.KeepAlive, false, true);

            var first = router.OpenAsync<TestView>(firstDefinition).GetAwaiter().GetResult();
            var second = router.ReplaceAsync<TestView>(secondDefinition).GetAwaiter().GetResult();

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(first.HideCount, Is.EqualTo(1));
            Assert.That(router.BackAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(router.BackAsync().GetAwaiter().GetResult(), Is.False);
            router.Dispose();
        }

        [Test]
        public void ADefinitionWhoseBackendProducesNoView_FailsTheOpenAndLeavesNothingBehind()
        {
            var views = new FakeViewFactory { ProduceNothing = true };
            var router = CreateRouter(views, UICachePolicy.KeepAlive, allowBack: true);

            Assert.Throws<InvalidOperationException>(() => router.OpenAsync<TestView>().GetAwaiter().GetResult());

            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        }

        [Test]
        public void GenericOpen_WithWrongDefinitionType_DisposesTheViewBeforeOpeningIt()
        {
            var views = new FakeViewFactory { ViewOverride = new OtherView() };
            var router = CreateRouter(views, UICachePolicy.KeepAlive, allowBack: true);
            var definition = new UiDefinition("wrong", new AddressViewSource("ui/wrong"), UILayer.Popup,
                UICachePolicy.KeepAlive, true, true);

            Assert.Throws<InvalidOperationException>(() => router.OpenAsync<TestView>(definition).GetAwaiter().GetResult());

            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
            Assert.That(views.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void DisposingTheRouter_ReleasesEveryViewItKeptOrShowed_Once()
        {
            var views = new FakeViewFactory();
            var router = CreateRouter(views, UICachePolicy.KeepAlive, allowBack: true);
            var view = router.OpenAsync<TestView>().GetAwaiter().GetResult();

            router.Dispose();

            Assert.That(view.Destroyed, Is.True);
            Assert.That(views.Disposals, Is.EqualTo(1));
        }

        private static UIRouter CreateRouter(FakeViewFactory views, UICachePolicy policy, bool allowBack)
        {
            var registry = new UIDefinitionRegistry();
            registry.Register<TestView>(new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.Popup, policy, true, allowBack));
            return new UIRouter(registry, views);
        }

        private sealed class FakeViewFactory : IUIViewFactory
        {
            public int Created { get; private set; }
            public int Disposals { get; private set; }
            public bool ProduceNothing { get; set; }
            public IUIView ViewOverride { get; set; }

            public UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
            {
                Created++;
                if (ProduceNothing)
                    return UniTask.FromResult<IUIViewInstance>(null);

                IUIView view = ViewOverride ?? new TestView();
                return UniTask.FromResult<IUIViewInstance>(new UIViewInstance(view, () =>
                {
                    Disposals++;
                    if (view is TestView testView)
                        testView.Destroyed = true;
                }));
            }
        }
    }
}
