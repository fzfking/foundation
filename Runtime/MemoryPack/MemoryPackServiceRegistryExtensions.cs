using System;
using Foundation.DI;
using Foundation.Serialization;

namespace Foundation.Infrastructure.MemoryPack
{
    public static class MemoryPackServiceRegistryExtensions
    {
        /// <summary><see cref="MemoryPackSerializerAdapter"/> as the application's <see cref="ISerializer"/>.</summary>
        public static IServiceRegistry UseMemoryPack(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<MemoryPackSerializerAdapter>(ServiceLifetime.Singleton).As<ISerializer>();
            return registry;
        }
    }
}
