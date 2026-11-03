using Foundation.DI;
using Foundation.Messaging;
using Foundation.Scheduling;
using VContainer;

namespace Foundation.Infrastructure.VContainer
{
    public static class FoundationContainerBuilderExtensions
    {
        /// <summary>
        /// The Foundation's view of a VContainer builder, for a project that owns its <c>LifetimeScope</c>:
        /// <c>new SomeInstaller().Install(builder.AsServiceRegistry())</c>.
        /// </summary>
        public static IServiceRegistry AsServiceRegistry(this IContainerBuilder builder)
            => new VContainerServiceRegistry(builder);

        /// <summary>The Foundation's view of a built VContainer resolver; the caller keeps ownership of the container.</summary>
        public static IServiceScope AsServiceScope(this IObjectResolver resolver)
            => new VContainerServiceScope(resolver, ownsScope: false);

        public static void Install(this IFeatureInstaller installer, IContainerBuilder builder)
            => installer.Install(builder.AsServiceRegistry());


        public static void RegisterMessaging(this IContainerBuilder builder)
            => builder.AsServiceRegistry().RegisterMessaging();

        public static void RegisterCommandHandler<TCommand, THandler>(this IContainerBuilder builder)
            where THandler : class, ICommandHandler<TCommand>
            => builder.AsServiceRegistry().RegisterCommandHandler<TCommand, THandler>();

        public static void RegisterQueryHandler<TQuery, TResult, THandler>(this IContainerBuilder builder)
            where THandler : class, IQueryHandler<TQuery, TResult>
            => builder.AsServiceRegistry().RegisterQueryHandler<TQuery, TResult, THandler>();

        public static void RegisterScheduler(this IContainerBuilder builder)
            => builder.AsServiceRegistry().RegisterScheduler();

        public static void RegisterScopedScheduler(this IContainerBuilder builder)
            => builder.AsServiceRegistry().RegisterScopedScheduler();

        /// <summary>A built VContainer resolver as a scope that disposes the container when it is disposed.</summary>
        public static IServiceScope AsOwningServiceScope(this IObjectResolver resolver)
            => new VContainerServiceScope(resolver, ownsScope: true);
    }
}
