using System;
using Foundation.DI;
using Foundation.Serialization;
using MessagePack;

namespace Foundation.Infrastructure.MessagePack
{
    public static class MessagePackServiceRegistryExtensions
    {
        /// <summary>
        /// <see cref="MessagePackSerializerAdapter"/> as the application's <see cref="ISerializer"/>; <paramref name="resolvers"/>
        /// are the game's own formatter resolvers.
        /// </summary>
        public static IServiceRegistry UseMessagePack(this IServiceRegistry registry, params IFormatterResolver[] resolvers)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<ISerializer>(_ => new MessagePackSerializerAdapter(resolvers), ServiceLifetime.Singleton);
            return registry;
        }
    }
}
