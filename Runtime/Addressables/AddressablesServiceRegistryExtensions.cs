using System;
using Foundation.Content;
using Foundation.DI;
using Foundation.Spawning;
using Foundation.Logging;

namespace Foundation.Infrastructure.Addressables
{
    public static class AddressablesServiceRegistryExtensions
    {
        /// <summary>
        /// The application's <see cref="IAssetProvider"/> on Addressables behind a <see cref="QueuedAssetProvider"/>, and an
        /// <see cref="IObjectSpawner"/> for the shared per-frame instantiation budget. Needs an <c>IAppLogger</c>.
        /// </summary>
        public static IServiceRegistry UseAddressables(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<IObjectSpawner>(_ => new AsyncObjectSpawner(AsyncObjectSpawner.DefaultIntegrationTimeMS), ServiceLifetime.Singleton);
            registry.Register(
                resolver => new QueuedAssetProvider(
                    new AddressablesProvider(
                        resolver.Resolve<IAppLogger>(),
                        resolver.Resolve<IObjectSpawner>())),
                ServiceLifetime.Singleton).As<IAssetProvider>();
            return registry;
        }
    }
}
