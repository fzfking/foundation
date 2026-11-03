using System;
using Foundation.DI;

namespace Foundation.Logging
{
    public static class LoggingServiceRegistryExtensions
    {
        /// <summary>
        /// The application's <see cref="IAppLogger"/> writing to <paramref name="outputs"/>, or to the Unity console when none is given.
        /// </summary>
        public static IServiceRegistry UseLogging(this IServiceRegistry registry, params ILogOutput[] outputs)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            var used = outputs is { Length: > 0 } 
                ? outputs 
                : new ILogOutput[] { new UnityLogOutput() };
            registry.Register<IAppLogger>(_ => new AppLogger(used), ServiceLifetime.Singleton);
            return registry;
        }
    }
}
