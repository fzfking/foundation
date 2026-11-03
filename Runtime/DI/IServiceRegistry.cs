using System;

namespace Foundation.DI
{
    /// <summary>
    /// What a feature installer registers into. Semantics every adapter must keep:
    /// <list type="bullet">
    /// <item>a registration is exposed as its own type until <see cref="IServiceBinding.As"/> says otherwise;
    /// <see cref="IServiceBinding.AsSelf"/> adds the type back;</item>
    /// <item>a child scope's registration shadows the same service from its parent;</item>
    /// <item>constructor parameters are resolved from the scope; there is no optional parameter, a possibly
    /// absent dependency goes through a factory that calls
    /// <see cref="ServiceResolverExtensions.TryResolve{T}"/>;</item>
    /// <item>resolving <c>IReadOnlyList&lt;T&gt;</c> yields every registration of <c>T</c> visible from the
    /// scope, parents' included.</item>
    /// </list>
    /// </summary>
    public interface IServiceRegistry
    {
        IServiceBinding Register(Type implementationType, ServiceLifetime lifetime);

        IServiceBinding Register<TImplementation>(ServiceLifetime lifetime);

        IServiceBinding Register<TService>(Func<IServiceResolver, TService> factory, ServiceLifetime lifetime);

        IServiceBinding RegisterInstance<TService>(TService instance);

        /// <summary>
        /// Registers <typeparamref name="TImplementation"/> as every interface it implements and itself, and
        /// runs its entry-point interfaces (<see cref="Lifecycle.IStartableEntryPoint"/>,
        /// <see cref="Lifecycle.IAsyncStartableEntryPoint"/>, <see cref="Lifecycle.ITickableEntryPoint"/>) from
        /// scope build to disposal.
        /// </summary>
        IServiceBinding RegisterEntryPoint<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton);

        IServiceBinding RegisterEntryPoint<TImplementation>(
            Func<IServiceResolver, TImplementation> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton);

        /// <summary>Runs once the scope is built, with the scope's resolver.</summary>
        void RegisterBuildCallback(Action<IServiceResolver> callback);

        /// <summary>Runs when the scope is disposed, with the scope's resolver.</summary>
        void RegisterDisposeCallback(Action<IServiceResolver> callback);
    }

    public interface IServiceBinding
    {
        IServiceBinding As(Type serviceType);

        IServiceBinding AsSelf();
    }

    public static class ServiceBindingExtensions
    {
        public static IServiceBinding As<TService>(this IServiceBinding binding)
        {
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));

            return binding.As(typeof(TService));
        }
    }
}
