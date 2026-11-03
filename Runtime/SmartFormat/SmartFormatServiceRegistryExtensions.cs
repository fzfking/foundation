using System;
using Foundation.DI;
using Foundation.Localization;
using SmartFormat;

namespace Foundation.Infrastructure.SmartFormat
{
    public static class SmartFormatServiceRegistryExtensions
    {
        /// <summary>
        /// <see cref="SmartFormatLocalizationFormatter"/> as the application's <see cref="ILocalizationFormatter"/>. Pair it
        /// with <see cref="LocalizationServiceRegistryExtensions.UseLocalization{TTableSource}"/>, which leaves the formatter
        /// to the project.
        /// </summary>
        /// <param name="configure">Adjusts the SmartFormat pipeline once, when the formatter is created.</param>
        public static IServiceRegistry UseSmartFormat(this IServiceRegistry registry, Action<SmartFormatter> configure = null)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.RegisterInstance<ILocalizationFormatter>(new SmartFormatLocalizationFormatter(configure));
            return registry;
        }
    }
}
