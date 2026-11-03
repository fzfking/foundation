using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.DI;
using Foundation.Spawning;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.UI.UGui
{
    /// <summary>
    /// The uGUI backend of the router: a view is a prefab (referenced by the definition or loaded by address
    /// through <see cref="IAssetProvider"/>), instantiated under its layer's root, injected with its scope's
    /// services and kept inactive until shown. The view is the <see cref="IUIView"/> component on the
    /// prefab's root.
    /// </summary>
    public sealed class UGuiViewFactory : IUIViewFactory
    {
        // Reused for every lookup (main thread only) instead of a component array per created view.
        private static readonly List<MonoBehaviour> ScratchBehaviours = new();

        private readonly IUIRoot _uiRoot;
        private readonly IAssetProvider _assetProvider;
        private readonly IGameObjectInjector _injector;
        private readonly IObjectSpawner _spawner;

        public UGuiViewFactory(
            IUIRoot uiRoot,
            IAssetProvider assetProvider,
            IGameObjectInjector injector = null,
            IObjectSpawner spawner = null)
        {
            _uiRoot = uiRoot ?? throw new ArgumentNullException(nameof(uiRoot));
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _injector = injector;
            _spawner = spawner ?? new AsyncObjectSpawner();
        }

        public async UniTask<IUIViewInstance> CreateAsync(UiDefinition definition, CancellationToken cancellationToken)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var (gameObject, lifetime) = await SpawnAsync(definition, cancellationToken);
            try
            {
                var view = FindView(gameObject);
                if (view == null)
                    throw new InvalidOperationException($"UI definition '{definition.ViewId}' does not contain an IUIView component.");

                // Inject before deactivating: Bind() re-applies state and runs only once, here. If Awake has not
                // run yet (an async instantiation path), deactivating first defers Awake until after this Inject,
                // stranding the component without services (a sprite-swap strategy would no-op forever for lack
                // of an asset provider).
                _injector?.Inject(gameObject);
                // Keep the freshly instantiated object out of the render loop until its view lifecycle and show animation are
                // ready to run.
                gameObject.SetActive(false);

                return new UIViewInstance(view, lifetime.Dispose);
            }
            catch
            {
                lifetime.Dispose();
                throw;
            }
        }

        private async UniTask<(GameObject GameObject, IDisposable Lifetime)> SpawnAsync(
            UiDefinition definition,
            CancellationToken cancellationToken)
        {
            var parent = _uiRoot.Resolve(definition.Layer);
            switch (definition.Source)
            {
                case PrefabViewSource prefab:
                    var spawned = await _spawner.InstantiateAsync(prefab.Prefab, parent, cancellationToken);
                    return (spawned, new DestroyOnDispose(spawned));
                case AddressViewSource address:
                    var instance = await _assetProvider.InstantiateAsync(
                        address.Address,
                        parent: parent,
                        cancellationToken: cancellationToken);
                    return (instance.GameObject, instance);
                default:
                    throw new InvalidOperationException(
                        $"UI definition '{definition.ViewId}' needs an {nameof(AddressViewSource)} or a {nameof(PrefabViewSource)} as its source.");
            }
        }

        private static IUIView FindView(GameObject instance)
        {
            instance.GetComponents(ScratchBehaviours);
            try
            {
                foreach (var behaviour in ScratchBehaviours)
                {
                    if (behaviour is IUIView view)
                        return view;
                }

                return null;
            }
            finally
            {
                ScratchBehaviours.Clear();
            }
        }

        private sealed class DestroyOnDispose : IDisposable
        {
            private GameObject _gameObject;

            public DestroyOnDispose(GameObject gameObject) => _gameObject = gameObject;

            public void Dispose()
            {
                if (_gameObject != null)
                    Object.Destroy(_gameObject);
                _gameObject = null;
            }
        }
    }
}
