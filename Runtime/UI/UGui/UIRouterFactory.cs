using System;
using Foundation.Content;
using Foundation.DI;
using Foundation.Spawning;

namespace Foundation.UI.UGui
{
    /// <summary>Creates routers over a uGUI root; each router is an independent navigation scope with its own stack and cache.</summary>
    public interface IUIRouterFactory
    {
        IUIRouter Create(IUIRoot root);
    }

    public sealed class UIRouterFactory : IUIRouterFactory
    {
        private readonly IUIDefinitionRegistry _registry;
        private readonly IAssetProvider _assetProvider;
        private readonly IGameObjectInjector _instanceInjector;
        private readonly IObjectSpawner _spawner;

        public UIRouterFactory(
            IUIDefinitionRegistry registry,
            IAssetProvider assetProvider,
            IGameObjectInjector instanceInjector = null,
            IObjectSpawner spawner = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _instanceInjector = instanceInjector;
            _spawner = spawner;
        }

        public IUIRouter Create(IUIRoot root)
        {
            return root == null 
                ? throw new ArgumentNullException(nameof(root)) 
                : new UIRouter(_registry, new UGuiViewFactory(root, _assetProvider, _instanceInjector, _spawner));
        }
    }
}
