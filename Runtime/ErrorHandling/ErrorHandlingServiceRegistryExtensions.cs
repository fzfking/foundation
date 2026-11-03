using System;
using Foundation.DI;

namespace Foundation.ErrorHandling
{
    public static class ErrorHandlingServiceRegistryExtensions
    {
        /// <summary><see cref="ErrorHandler"/> as <see cref="IErrorHandler"/>. Needs an <c>IAppLogger</c> and an <c>IEventBus</c>.</summary>
        public static IServiceRegistry UseErrorHandling(this IServiceRegistry registry)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<ErrorHandler>(ServiceLifetime.Singleton).As<IErrorHandler>();
            return registry;
        }
    }
}
