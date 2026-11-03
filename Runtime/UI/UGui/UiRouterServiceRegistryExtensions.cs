using System;
using Foundation.DI;

namespace Foundation.UI.UGui
{
    public static class UiRouterServiceRegistryExtensions
    {
        /// <summary>
        /// A uGUI router over <paramref name="definitions"/>, parented under <paramref name="root"/>. Needs an
        /// <see cref="Foundation.Content.IAssetProvider"/>; an <see cref="IGameObjectInjector"/> is used when registered.
        /// </summary>
        public static IServiceRegistry UseUiRouter(this IServiceRegistry registry, IUIDefinitionRegistry definitions, IUIRoot root)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            if (root == null)
                throw new ArgumentNullException(nameof(root));

            registry.RegisterInstance(definitions);
            registry.Register<UIRouterFactory>(ServiceLifetime.Singleton).As<IUIRouterFactory>();
            registry.Register<IUIRouter>(resolver => resolver.Resolve<IUIRouterFactory>().Create(root), ServiceLifetime.Singleton);
            return registry;
        }
    }
}
