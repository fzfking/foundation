using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.UI;
using Foundation.UI.UGui;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.UI
{
    public sealed class UGuiRouterTests
    {
        private sealed class TestView : MonoBehaviour, IUIView
        {
            public bool WasActiveWhenInjected { get; set; }

            public UniTask ShowAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;

            public UniTask HideAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        }

        [Test]
        public void RouterFactory_CreatesIndependentScopes()
        {
            var provider = new FakeAssetProvider();
            var registry = new UIDefinitionRegistry();
            registry.Register<TestView>(new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.FullScreen, UICachePolicy.KeepAlive, true, true));
            var factory = new UIRouterFactory(registry, provider);

            var first = factory.Create(new FakeUIRoot());
            var second = factory.Create(new FakeUIRoot());
            var firstView = first.OpenAsync<TestView>().GetAwaiter().GetResult();
            var secondView = second.OpenAsync<TestView>().GetAwaiter().GetResult();

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(secondView, Is.Not.SameAs(firstView));
            Assert.That(provider.InstantiateCount, Is.EqualTo(2));
            first.Dispose();
            second.Dispose();
        }

        [Test]
        public void ACreatedView_IsInjectedWhileStillActive_ThenLeftInactiveUntilShown()
        {
            var provider = new FakeAssetProvider();
            var injector = new RecordingInjector();
            var factory = new UGuiViewFactory(new FakeUIRoot(), provider, injector);
            var definition = new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.Popup, UICachePolicy.DestroyOnClose, true, true);

            var instance = factory.CreateAsync(definition, CancellationToken.None).GetAwaiter().GetResult();
            var view = (TestView)instance.View;

            Assert.That(injector.InjectedWhileActive, Is.True, "services must arrive before the object is deactivated");
            Assert.That(view.gameObject.activeSelf, Is.False);

            instance.Dispose();
            Assert.That(provider.LastInstance.Disposed, Is.True);
        }

        [Test]
        public void APrefabWithoutAViewComponent_FailsAndIsNotLeftInTheScene()
        {
            var provider = new FakeAssetProvider { AddView = false };
            var factory = new UGuiViewFactory(new FakeUIRoot(), provider);
            var definition = new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.Popup, UICachePolicy.DestroyOnClose, true, true);

            Assert.Throws<InvalidOperationException>(() => factory.CreateAsync(definition, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(provider.LastInstance.Disposed, Is.True);
        }

        [Test]
        public void InjectionFailure_DisposesTheSpawnedInstance()
        {
            var provider = new FakeAssetProvider();
            var injector = new RecordingInjector { ThrowOnInject = true };
            var factory = new UGuiViewFactory(new FakeUIRoot(), provider, injector);
            var definition = new UiDefinition("test", new AddressViewSource("ui/test"), UILayer.Popup,
                UICachePolicy.DestroyOnClose, true, true);

            Assert.Throws<InvalidOperationException>(() => factory.CreateAsync(definition, CancellationToken.None).GetAwaiter().GetResult());

            Assert.That(provider.LastInstance.Disposed, Is.True);
        }

        private sealed class RecordingInjector : IGameObjectInjector
        {
            public bool InjectedWhileActive { get; private set; }
            public bool ThrowOnInject { get; set; }

            public void Inject(GameObject instance)
            {
                InjectedWhileActive = instance.activeSelf;
                if (ThrowOnInject)
                    throw new InvalidOperationException("Injection failed.");
            }
        }

        private sealed class FakeUIRoot : IUIRoot
        {
            public Transform Resolve(UILayer layer) => null;
        }

        private sealed class FakeAssetProvider : IAssetProvider
        {
            public bool AddView { get; set; } = true;
            public int InstantiateCount { get; private set; }
            public FakeAssetInstance LastInstance { get; private set; }

            public bool ContainsAsset<T>(string assetId) where T : Object
                => throw new NotSupportedException();

            public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
                where T : Object => throw new NotSupportedException();

            public UniTask<IPreloadHandle> PreloadAsync(
                IReadOnlyList<string> assetIds,
                IProgress<float> progress = null,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public UniTask<IAssetInstance> InstantiateAsync(string assetId, Transform parent = null, CancellationToken cancellationToken = default)
            {
                InstantiateCount++;
                var gameObject = new GameObject(assetId);
                if (AddView)
                    gameObject.AddComponent<TestView>();
                LastInstance = new(gameObject);
                return UniTask.FromResult<IAssetInstance>(LastInstance);
            }

            public void Dispose() { }
        }

        private sealed class FakeAssetInstance : IAssetInstance
        {
            public GameObject GameObject { get; }
            public bool Disposed { get; private set; }

            public FakeAssetInstance(GameObject gameObject) => GameObject = gameObject;

            public void Dispose()
            {
                Disposed = true;
                if (GameObject != null)
                    Object.DestroyImmediate(GameObject);
            }
        }
    }
}
