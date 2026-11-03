using System;
using Foundation.Content;
using Foundation.DI;
using Foundation.Serialization;

namespace Foundation.Config
{
    public static class ConfigServiceRegistryExtensions
    {
        /// <summary>
        /// The config runtime: group registry, repository, provider and resolver. The provider needs an
        /// <see cref="ISerializer"/> and an <see cref="IAssetProvider"/>, registered separately.
        /// </summary>
        public static IServiceRegistry UseConfigs(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<ConfigGroupRegistry>(ServiceLifetime.Singleton).As<IConfigGroupRegistry>();
            registry.Register<ConfigRepository>(ServiceLifetime.Singleton).As<IConfigRepository>();
            SerializedConfigProvider provider = null;
            registry.Register<SerializedConfigProvider>(resolver => provider = new SerializedConfigProvider(
                resolver.Resolve<IAssetProvider>(),
                resolver.Resolve<ISerializer>(),
                resolver.Resolve<IConfigRepository>(),
                resolver.Resolve<IConfigGroupRegistry>()), ServiceLifetime.Singleton).As<IConfigProvider>();
            registry.RegisterDisposeCallback(_ => provider?.Shutdown());
            registry.Register<ConfigResolver>(ServiceLifetime.Singleton).As<IConfigResolver>();
            return registry;
        }

        /// <summary>Declares the groups of <paramref name="installer"/> once the scope is built.</summary>
        public static IServiceRegistry UseConfigGroups(this IServiceRegistry registry, IConfigGroupInstaller installer)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (installer == null)
                throw new ArgumentNullException(nameof(installer));

            registry.RegisterBuildCallback(resolver => installer.Install(resolver.Resolve<IConfigGroupRegistry>()));
            return registry;
        }
    }
}
