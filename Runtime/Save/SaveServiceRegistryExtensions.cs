using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.GameContexts;

namespace Foundation.Save
{
    public static class SaveServiceRegistryExtensions
    {
        /// <summary>
        /// The persistence plumbing for the application scope: <see cref="SaveSerializer"/> over the registered
        /// <see cref="Foundation.Serialization.ISerializer"/>, a local file in the persistent data folder, and a
        /// <see cref="CompositeSaveRepository"/> over every registered <see cref="ISaveRepositoryBackend"/>. Needs a container
        /// that resolves a collection of a service.
        /// </summary>
        public static IServiceRegistry UseSaveStorage(this IServiceRegistry registry, string defaultFileName = "save.dat")
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.RegisterInstance<ILocalSaveLocation>(new PersistentDataSaveLocation(defaultFileName));
            registry.Register<LocalFileSaveRepository>(ServiceLifetime.Singleton).As<ISaveRepositoryBackend>();
            registry.Register<SaveSerializer>(ServiceLifetime.Singleton).As<ISaveSerializer>();
            registry.Register(
                resolver => new CompositeSaveRepository(resolver.Resolve<IReadOnlyList<ISaveRepositoryBackend>>()),
                ServiceLifetime.Singleton).As<ISaveRepository>();
            return registry;
        }

        /// <summary>
        /// A <see cref="SaveOrchestrator"/> over every <see cref="IFeatureSaveModule"/> the scope sees; register it where the modules
        /// are. A registered GameContextSaveProfile supplies the slot, version and migrations; otherwise the helper defaults apply.
        /// </summary>
        public static IServiceRegistry UseSaveOrchestrator(
            this IServiceRegistry registry,
            int currentVersion = SaveOrchestrator.CurrentVersion,
            params ISaveMigration[] migrations)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register(
                resolver =>
                {
                    resolver.TryResolve<GameContextSaveProfile>(out var profile);
                    return new SaveOrchestrator(
                        resolver.Resolve<IReadOnlyList<IFeatureSaveModule>>(),
                        resolver.Resolve<ISaveRepository>(),
                        resolver.Resolve<ISaveSerializer>(),
                        profile?.SaveVersion ?? currentVersion,
                        profile?.Migrations ?? migrations,
                        profile?.SlotId ?? SaveSlot.Default);
                },
                ServiceLifetime.Singleton);
            return registry;
        }

        /// <summary>Registers <typeparamref name="TModule"/> as a save module, and as itself.</summary>
        public static IServiceRegistry UseSaveModule<TModule>(this IServiceRegistry registry)
            where TModule : class, IFeatureSaveModule
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<TModule>(ServiceLifetime.Singleton).AsSelf().As<IFeatureSaveModule>();
            return registry;
        }
    }
}
