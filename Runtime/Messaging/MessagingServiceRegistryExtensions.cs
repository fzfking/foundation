using System;
using Foundation.DI;

namespace Foundation.Messaging
{
    /// <summary>
    /// Composition of the buses. The application scope calls <see cref="RegisterMessaging"/> once; a feature
    /// or game context registers handlers into its own scope and they are removed when it is disposed, so a
    /// context can be left and re-entered with no stale handlers.
    /// </summary>
    public static class MessagingServiceRegistryExtensions
    {
        /// <summary>
        /// The application's buses: events fan out to any number of subscribers through the in-process
        /// <see cref="EventBus"/>; commands and queries go to their one handler. <paramref name="options"/> sets the
        /// ordering (default: handlers of an event one after another, commands immediate).
        /// </summary>
        public static void RegisterMessaging(this IServiceRegistry registry, MessagingOptions options = null)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            options ??= MessagingOptions.Default;
            registry.Register(_ => new EventBus(options), ServiceLifetime.Singleton).As<IEventBus>();
            registry.RegisterCommandAndQueryBuses(options);
        }

        /// <summary><see cref="RegisterMessaging"/>, chainable.</summary>
        public static IServiceRegistry UseMessaging(this IServiceRegistry registry, MessagingOptions options = null)
        {
            registry.RegisterMessaging(options);
            return registry;
        }

        /// <summary>The command and query buses alone, for an event bus that is registered separately.</summary>
        public static void RegisterCommandAndQueryBuses(this IServiceRegistry registry, MessagingOptions options = null)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            options ??= MessagingOptions.Default;
            registry.Register(_ => new CommandBus(options), ServiceLifetime.Singleton).As<ICommandBus>().As<ICommandHandlerRegistry>();
            registry.Register<QueryBus>(ServiceLifetime.Singleton).As<IQueryBus>().As<IQueryHandlerRegistry>();
        }

        /// <summary>
        /// Makes <typeparamref name="THandler"/> the handler of <typeparamref name="TCommand"/> from scope
        /// build until disposal.
        /// </summary>
        public static void RegisterCommandHandler<TCommand, THandler>(this IServiceRegistry registry)
            where THandler : class, ICommandHandler<TCommand>
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<THandler>(ServiceLifetime.Singleton);
            IDisposable registration = null;
            registry.RegisterBuildCallback(resolver =>
                registration = resolver.Resolve<ICommandHandlerRegistry>().RegisterHandler<TCommand>(resolver.Resolve<THandler>()));
            registry.RegisterDisposeCallback(_ => registration?.Dispose());
        }

        /// <summary>
        /// Makes <typeparamref name="THandler"/> the handler of <typeparamref name="TQuery"/> with result
        /// <typeparamref name="TResult"/> while the scope lives.
        /// </summary>
        public static void RegisterQueryHandler<TQuery, TResult, THandler>(this IServiceRegistry registry)
            where THandler : class, IQueryHandler<TQuery, TResult>
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<THandler>(ServiceLifetime.Singleton);
            IDisposable registration = null;
            registry.RegisterBuildCallback(resolver =>
                registration = resolver.Resolve<IQueryHandlerRegistry>().RegisterHandler<TQuery, TResult>(resolver.Resolve<THandler>()));
            registry.RegisterDisposeCallback(_ => registration?.Dispose());
        }
    }
}
