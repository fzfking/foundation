using System;
using Foundation.DI;
using Foundation.Logging;

namespace Foundation.Localization
{
    public static class LocalizationServiceRegistryExtensions
    {
        /// <summary>
        /// <see cref="LocalizationService"/> over <typeparamref name="TTableSource"/> and <typeparamref name="TFormatter"/>, exposed as
        /// <see cref="ILocalizationService"/> and <see cref="ILocalizationSource"/>. Initialising it with a locale (see
        /// <see cref="ILocalizationService.InitializeAsync"/>) is the project's startup step.
        /// </summary>
        public static IServiceRegistry UseLocalization<TTableSource, TFormatter>(
            this IServiceRegistry registry,
            LocalizationSettings settings)
            where TTableSource : class, ILocalizationTableSource
            where TFormatter : class, ILocalizationFormatter
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            registry.UseLocalization<TTableSource>(settings);
            registry.Register<TFormatter>(ServiceLifetime.Singleton).As<ILocalizationFormatter>();
            return registry;
        }

        /// <summary>
        /// The same without the formatter: register an <see cref="ILocalizationFormatter"/> yourself, or use an adapter's
        /// (for example <c>UseSmartFormat</c>).
        /// </summary>
        public static IServiceRegistry UseLocalization<TTableSource>(
            this IServiceRegistry registry,
            LocalizationSettings settings)
            where TTableSource : class, ILocalizationTableSource
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            registry.RegisterInstance(settings);
            registry.Register<TTableSource>(ServiceLifetime.Singleton).As<ILocalizationTableSource>();
            // The logger is optional: a scope without one still gets the service, which logs to the Unity console instead.
            registry.Register(
                resolver => new LocalizationService(
                    resolver.Resolve<ILocalizationTableSource>(),
                    resolver.Resolve<ILocalizationFormatter>(),
                    settings,
                    resolver.TryResolve(out IAppLogger logger) ? logger : null),
                ServiceLifetime.Singleton).As<ILocalizationService>().As<ILocalizationSource>();
            return registry;
        }
    }
}
