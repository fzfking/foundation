using System;
using Foundation.DI;

namespace Foundation.FeatureFlags
{
    public static class FeatureFlagServiceRegistryExtensions
    {
        /// <summary><see cref="FeatureFlagService"/> reading its flags from <typeparamref name="TSource"/>.</summary>
        public static IServiceRegistry UseFeatureFlags<TSource>(this IServiceRegistry registry)
            where TSource : class, IFeatureFlagSource
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<TSource>(ServiceLifetime.Singleton).As<IFeatureFlagSource>();
            registry.Register<FeatureFlagService>(ServiceLifetime.Singleton).As<IFeatureFlagService>();
            return registry;
        }

        /// <summary><see cref="FeatureFlagService"/> reading its flags from <paramref name="source"/>.</summary>
        public static IServiceRegistry UseFeatureFlags(this IServiceRegistry registry, IFeatureFlagSource source)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            registry.RegisterInstance(source);
            registry.Register<FeatureFlagService>(ServiceLifetime.Singleton).As<IFeatureFlagService>();
            return registry;
        }
    }
}
