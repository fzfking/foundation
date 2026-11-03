using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.TestTools;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.UI;
using NUnit.Framework;

namespace Foundation.Tests.UI
{
    public sealed class UIRouterLifetimeTests
    {
        [Test]
        public void LateFactoryResult_IsDisposedAndNeverShownAfterRouterDisposal()
        {
            var factory = new Factory { Pending = new UniTaskCompletionSource<IUIViewInstance>() };
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var pending = router.OpenAsync(Definition("late")).Preserve();
            router.Dispose();
            var view = factory.CreateView();
            factory.Pending.TrySetResult(view);

            Assert.Catch<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.That(view.Disposals, Is.EqualTo(1));
            Assert.That(view.Shows, Is.Zero);
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        }

        [UnityTest]
        public IEnumerator DisposingWithAQueuedOpen_CancelsBothOperations() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory { Pending = new UniTaskCompletionSource<IUIViewInstance>() };
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var first = router.OpenAsync(Definition("first")).Preserve();
            var queued = router.OpenAsync(Definition("queued")).Preserve();
            router.Dispose();
            var view = factory.CreateView();
            factory.Pending.TrySetResult(view);

            Assert.That((await first.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That((await queued.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(factory.Created.Count, Is.EqualTo(1));
            Assert.That(view.Disposals, Is.EqualTo(1));
        });

        [Test]
        public void CancelledClose_RetainsOwnershipAndCanBeRetried()
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)router.OpenAsync(Definition("one")).GetAwaiter().GetResult();
            view.FailHide = true;

            Assert.Catch<OperationCanceledException>(() => router.CloseAsync(view).GetAwaiter().GetResult());
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);
            view.FailHide = false;
            router.CloseAsync(view).GetAwaiter().GetResult();

            Assert.That(view.Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        }

        [Test]
        public void PopupHandle_CancelledCloseCanBeRetried()
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var handle = PopupRouting.OpenPopupAsync<View>(router, Definition("one")).GetAwaiter().GetResult();
            handle.View.FailHide = true;

            Assert.Catch<OperationCanceledException>(() => handle.CloseAsync().GetAwaiter().GetResult());
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);

            handle.View.FailHide = false;
            handle.CloseAsync().GetAwaiter().GetResult();

            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
            Assert.That(handle.View.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void CancelledBack_RetainsTheViewUntilRouterDisposal()
        {
            var factory = new Factory();
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)router.OpenAsync(Definition("one")).GetAwaiter().GetResult();
            view.FailHide = true;
            Assert.Catch<OperationCanceledException>(() => router.BackAsync().GetAwaiter().GetResult());
            router.Dispose();
            Assert.That(view.Disposals, Is.EqualTo(1));
        }

        [Test]
        public void FailedReplace_WhenOutgoingHideFails_KeepsTargetAndOutgoingViewOwned()
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var outgoing = (View)router.OpenAsync(Definition("old")).GetAwaiter().GetResult();
            outgoing.FailHide = true;
            Assert.Catch<OperationCanceledException>(() => router.ReplaceAsync(Definition("new")).GetAwaiter().GetResult());

            Assert.That(outgoing.Disposals, Is.Zero);
            Assert.That(factory.Created[1].Disposals, Is.Zero);
            Assert.That(factory.Created[1].Shows, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);

            outgoing.FailHide = false;
            router.ClearAsync().GetAwaiter().GetResult();
            Assert.That(outgoing.Disposals, Is.EqualTo(1));
            Assert.That(factory.Created[1].Disposals, Is.EqualTo(1));
        }

        [Test]
        public void FailedReplaceShow_KeepsTheOutgoingViewOpen()
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var outgoing = (View)router.OpenAsync(Definition("old")).GetAwaiter().GetResult();
            factory.FailShow = true;

            Assert.Throws<InvalidOperationException>(() => router.ReplaceAsync(Definition("new")).GetAwaiter().GetResult());

            Assert.That(outgoing.Disposals, Is.Zero);
            Assert.That(factory.Created[1].Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);
        }

        [UnityTest]
        public IEnumerator Replace_CancellationAfterTargetShow_KeepsBothEntriesOwned() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var outgoing = (View)await router.OpenAsync(Definition("old"));
            factory.ShowGate = new UniTaskCompletionSource();
            using var cancellation = new CancellationTokenSource();
            var pending = router.ReplaceAsync(Definition("new"), cancellationToken: cancellation.Token).Preserve();
            var target = factory.Created[1];

            cancellation.Cancel();
            factory.ShowGate.TrySetResult();

            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(target.Shows, Is.EqualTo(1));
            Assert.That(target.Disposals, Is.Zero);
            Assert.That(outgoing.Disposals, Is.Zero);
            Assert.That(outgoing.Hides, Is.Zero, "cancellation stops before the next Hide");
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);

            await router.ClearAsync();
            Assert.That(target.Disposals, Is.EqualTo(1));
            Assert.That(outgoing.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Replace_WhenCallerCancelsAfterTargetShowWithNoOutgoing_StillCommits() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory { ShowGate = new UniTaskCompletionSource() };
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            using var cancellation = new CancellationTokenSource();
            var pending = router.ReplaceAsync(Definition("target"), cancellationToken: cancellation.Token).Preserve();
            var target = factory.Created[0];

            cancellation.Cancel();
            factory.ShowGate.TrySetResult();

            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.False);
            Assert.That(target.Shows, Is.EqualTo(1));
            Assert.That(target.Disposals, Is.Zero);
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);
        });

        [UnityTest]
        public IEnumerator Replace_DisposingDuringTargetShow_CancelsBeforeChangingTheStack() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var outgoing = (View)await router.OpenAsync(Definition("old"));
            factory.ShowGate = new UniTaskCompletionSource();
            var pending = router.ReplaceAsync(Definition("new")).Preserve();
            var target = factory.Created[1];

            router.Dispose();

            Assert.That(target.Disposals, Is.Zero);
            factory.ShowGate.TrySetResult();

            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(target.Shows, Is.EqualTo(1));
            Assert.That(target.Disposals, Is.EqualTo(1));
            Assert.That(outgoing.Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        });

        [Test]
        public void FailedShow_ReleasesTheNewView()
        {
            var factory = new Factory { FailShow = true };
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            Assert.Throws<InvalidOperationException>(() => router.OpenAsync(Definition("one")).GetAwaiter().GetResult());
            Assert.That(factory.Created[0].Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        }

        [Test]
        public void Dispose_WhenOneViewThrows_StillAttemptsEveryOwnedView()
        {
            var factory = new Factory();
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var first = (View)router.OpenAsync(Definition("first")).GetAwaiter().GetResult();
            var second = (View)router.OpenAsync(Definition("second")).GetAwaiter().GetResult();
            first.ThrowOnDispose = true;

            Assert.Throws<InvalidOperationException>(router.Dispose);
            Assert.DoesNotThrow(router.Dispose);

            Assert.That(first.Disposals, Is.EqualTo(1));
            Assert.That(second.Disposals, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClearingWithABackHandlerInFlight_CancelsItAndDefersViewDisposal() => UniTask.ToCoroutine(async () =>
        {
            var factory = new BackFactory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            await router.OpenAsync(Definition("back"));
            var back = router.BackAsync().Preserve();
            await UniTask.Yield();

            await router.ClearAsync();

            Assert.That(factory.View.BackToken.IsCancellationRequested, Is.True);
            Assert.That(factory.View.Disposals, Is.Zero, "the handler still has a live view until its task completes");
            factory.View.Complete.TrySetResult(true);
            Assert.That(await back, Is.True);
            Assert.That(factory.View.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator DisposingWithShowInFlight_DefersViewDisposalUntilShowCompletes() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory { ShowGate = new UniTaskCompletionSource() };
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var pending = router.OpenAsync(Definition("show")).Preserve();
            var view = factory.Created[0];

            router.Dispose();

            Assert.That(view.Disposals, Is.Zero);
            factory.ShowGate.TrySetResult();
            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(view.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator DisposingWithHideInFlight_DefersViewDisposalUntilHideCompletes() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)await router.OpenAsync(Definition("hide"));
            view.HideGate = new UniTaskCompletionSource();
            var pending = router.CloseAsync(view).Preserve();

            router.Dispose();

            Assert.That(view.Disposals, Is.Zero);
            view.HideGate.TrySetResult();
            Assert.That(await pending.SuppressCancellationThrow(), Is.True);
            Assert.That(view.Disposals, Is.EqualTo(1));
        });

        [UnityTest]
        public IEnumerator Close_WhenCallerCancelsAfterHideSucceeded_StillRemovesTheEntry() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)await router.OpenAsync(Definition("one"));
            view.HideGate = new UniTaskCompletionSource();
            using var cancellation = new CancellationTokenSource();
            var pending = router.CloseAsync(view, cancellation.Token).Preserve();

            cancellation.Cancel();
            view.HideGate.TrySetResult();

            Assert.That(await pending.SuppressCancellationThrow(), Is.False);
            Assert.That(view.Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        });

        [UnityTest]
        public IEnumerator Open_WhenCallerCancelsAfterShowSucceeded_StillCommitsTheOpen() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory { ShowGate = new UniTaskCompletionSource() };
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            using var cancellation = new CancellationTokenSource();
            var pending = router.OpenAsync(Definition("open"), cancellationToken: cancellation.Token).Preserve();
            var view = factory.Created[0];

            cancellation.Cancel();
            factory.ShowGate.TrySetResult();

            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.False);
            Assert.That(view.Shows, Is.EqualTo(1));
            Assert.That(view.Disposals, Is.Zero);
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);
        });

        [UnityTest]
        public IEnumerator MergedOpen_WhenCallerCancelsAfterShowSucceeded_StillReportsSuccess() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            using var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)await router.OpenAsync(Definition("merge"));
            view.ShowGate = new UniTaskCompletionSource();
            using var cancellation = new CancellationTokenSource();
            var pending = router.OpenAsync(Definition("merge"), cancellationToken: cancellation.Token).Preserve();

            cancellation.Cancel();
            view.ShowGate.TrySetResult();

            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.False);
            Assert.That(view.Shows, Is.EqualTo(2));
            Assert.That(view.Disposals, Is.Zero);
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.True);
        });

        [UnityTest]
        public IEnumerator DisposingWithMergedShowInFlight_CancelsOpenAndDisposesTheViewOnce() => UniTask.ToCoroutine(async () =>
        {
            var factory = new Factory();
            var router = new UIRouter(new UIDefinitionRegistry(), factory);
            var view = (View)await router.OpenAsync(Definition("merge"));
            view.ShowGate = new UniTaskCompletionSource();
            var pending = router.OpenAsync(Definition("merge")).Preserve();

            router.Dispose();

            Assert.That(view.Disposals, Is.Zero);
            view.ShowGate.TrySetResult();
            Assert.That((await pending.SuppressCancellationThrow()).IsCanceled, Is.True);
            Assert.That(view.Disposals, Is.EqualTo(1));
            Assert.That(router.HasOpenViews(UILayer.Popup), Is.False);
        });

        [Test]
        public void KeepAliveWithDuplicates_IsRejectedAtDefinitionTime()
            => Assert.Throws<ArgumentException>(() => new UiDefinition("invalid", new AddressViewSource("ui"),
                UILayer.Popup, UICachePolicy.KeepAlive, false, true, allowDuplicates: true));

        private static UiDefinition Definition(string id)
            => new(id, new AddressViewSource(id), UILayer.Popup, UICachePolicy.DestroyOnClose, false, true);

        private sealed class Factory : IUIViewFactory
        {
            public readonly List<View> Created = new();
            public UniTaskCompletionSource<IUIViewInstance> Pending;
            public UniTaskCompletionSource ShowGate;
            public UniTaskCompletionSource HideGate;
            public bool FailShow;

            public View CreateView()
            {
                var view = new View { FailShow = FailShow, ShowGate = ShowGate, HideGate = HideGate };
                Created.Add(view);
                return view;
            }

            public UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
                => Pending == null ? UniTask.FromResult<IUIViewInstance>(CreateView()) : Pending.Task;
        }

        private sealed class View : IUIView, IUIViewInstance
        {
            public bool FailHide;
            public bool FailShow;
            public int Disposals;
            public int Hides;
            public int Shows;
            public bool ThrowOnDispose;
            public UniTaskCompletionSource ShowGate;
            public UniTaskCompletionSource HideGate;
            IUIView IUIViewInstance.View => this;

            public async UniTask ShowAsync(CancellationToken cancellationToken = default)
            {
                Shows++;
                if (ShowGate != null)
                    await ShowGate.Task;
                if (FailShow)
                    throw new InvalidOperationException("Cannot show.");
            }

            public async UniTask HideAsync(CancellationToken cancellationToken = default)
            {
                Hides++;
                if (FailHide)
                    throw new OperationCanceledException();
                if (HideGate != null)
                    await HideGate.Task;
            }

            public void Dispose()
            {
                Disposals++;
                if (ThrowOnDispose)
                    throw new InvalidOperationException("Injected view disposal failure.");
            }
        }

        private sealed class BackFactory : IUIViewFactory
        {
            public readonly BackView View = new();

            public UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
                => UniTask.FromResult<IUIViewInstance>(View);
        }

        private sealed class BackView : IUIView, IUIViewInstance, IUIBackHandler
        {
            public readonly UniTaskCompletionSource<bool> Complete = new();
            public CancellationToken BackToken;
            public int Disposals;
            IUIView IUIViewInstance.View => this;

            public UniTask ShowAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
            public UniTask HideAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;

            public UniTask<bool> HandleBackAsync(CancellationToken cancellationToken = default)
            {
                BackToken = cancellationToken;
                return Complete.Task;
            }

            public void Dispose() => Disposals++;
        }
    }
}
