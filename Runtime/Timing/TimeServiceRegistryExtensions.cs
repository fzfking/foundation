using System;
using Foundation.DI;

namespace Foundation.Timing
{
    public static class TimeServiceRegistryExtensions
    {
        /// <summary>Unity's clock: <see cref="SystemTimeProvider"/> as <see cref="ITimeProvider"/>, <see cref="UnityTimeController"/> as <see cref="ITimeController"/>.</summary>
        public static IServiceRegistry UseTime(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<SystemTimeProvider>(ServiceLifetime.Singleton).As<ITimeProvider>();
            registry.Register<UnityTimeController>(ServiceLifetime.Singleton).As<ITimeController>();
            return registry;
        }
    }
}
