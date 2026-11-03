using System.Threading;
using System;
using Foundation.DI;
using Foundation.Lifecycle;
using VContainer;
using VContainer.Unity;
using Cysharp.Threading.Tasks;

namespace Foundation.Infrastructure.VContainer
{
    /// <summary>
    /// <see cref="IServiceRegistry"/> over a VContainer <see cref="IContainerBuilder"/>. Also makes the
    /// scope's <see cref="IServiceResolver"/>, <see cref="IServiceScope"/> and
    /// <see cref="IGameObjectInjector"/> resolvable, once per scope, so Foundation services can take them as
    /// constructor parameters.
    /// </summary>
    public sealed class VContainerServiceRegistry : IServiceRegistry
    {
        private readonly IContainerBuilder _builder;

        public IContainerBuilder Builder => _builder;

        public VContainerServiceRegistry(IContainerBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
            RegisterScopeServices();
        }

        public IServiceBinding Register(Type implementationType, ServiceLifetime lifetime)
            => new Binding(_builder.Register(implementationType, ToLifetime(lifetime)));

        public IServiceBinding Register<TImplementation>(ServiceLifetime lifetime)
            => new Binding(_builder.Register<TImplementation>(ToLifetime(lifetime)));

        public IServiceBinding Register<TService>(Func<IServiceResolver, TService> factory, ServiceLifetime lifetime)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            return new Binding(_builder.Register<TService>(resolver => factory(Wrap(resolver)), ToLifetime(lifetime)));
        }

        public IServiceBinding RegisterInstance<TService>(TService instance)
            => new Binding(_builder.RegisterInstance<TService>(instance));

        public IServiceBinding RegisterEntryPoint<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            var binding = new Binding(_builder.Register<TImplementation>(ToLifetime(lifetime)).AsImplementedInterfaces().AsSelf());
            RegisterEntryPointBridges<TImplementation>();
            return binding;
        }

        public IServiceBinding RegisterEntryPoint<TImplementation>(
            Func<IServiceResolver, TImplementation> factory,
            ServiceLifetime lifetime = ServiceLifetime.Singleton)
        {
            if (factory == null)
                throw new ArgumentNullException(nameof(factory));

            var binding = new Binding(
                _builder.Register<TImplementation>(resolver => factory(Wrap(resolver)), ToLifetime(lifetime))
                    .AsImplementedInterfaces()
                    .AsSelf());
            RegisterEntryPointBridges<TImplementation>();
            return binding;
        }

        public void RegisterBuildCallback(Action<IServiceResolver> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _builder.RegisterBuildCallback(resolver => callback(Wrap(resolver)));
        }

        public void RegisterDisposeCallback(Action<IServiceResolver> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _builder.RegisterDisposeCallback(resolver => callback(Wrap(resolver)));
        }

        internal static Lifetime ToLifetime(ServiceLifetime lifetime)
        {
            switch (lifetime)
            {
                case ServiceLifetime.Transient: return Lifetime.Transient;
                case ServiceLifetime.Scoped: return Lifetime.Scoped;
                case ServiceLifetime.Singleton: return Lifetime.Singleton;
                default: throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null);
            }
        }

        // A view over the resolver the container hands a factory or callback; it is not the owner of that scope.
        private static IServiceScope Wrap(IObjectResolver resolver) => new VContainerServiceScope(resolver, ownsScope: false);

        private void RegisterScopeServices()
        {
            if (_builder.Exists(typeof(IServiceScope)))
                return;

            _builder.Register<IServiceScope>(resolver => Wrap(resolver), Lifetime.Scoped).As(typeof(IServiceResolver));
            _builder.Register<IGameObjectInjector>(
                resolver => new VContainerGameObjectInjector(resolver, Wrap(resolver)),
                Lifetime.Scoped);
        }

        // VContainer dispatches its own entry-point interfaces; the Foundation's are forwarded by one bridge
        // each. A bridge is generic over its entry point because VContainer refuses two registrations of one
        // implementation type.
        private void RegisterEntryPointBridges<TImplementation>()
        {
            if (typeof(IStartableEntryPoint).IsAssignableFrom(typeof(TImplementation)))
            {
                _builder.RegisterEntryPoint<StartableBridge<TImplementation>>(
                    resolver => new StartableBridge<TImplementation>((IStartableEntryPoint)resolver.Resolve<TImplementation>()),
                    Lifetime.Singleton);
            }

            if (typeof(IAsyncStartableEntryPoint).IsAssignableFrom(typeof(TImplementation)))
            {
                _builder.RegisterEntryPoint<AsyncStartableBridge<TImplementation>>(
                    resolver => new AsyncStartableBridge<TImplementation>((IAsyncStartableEntryPoint)resolver.Resolve<TImplementation>()),
                    Lifetime.Singleton);
            }

            if (typeof(ITickableEntryPoint).IsAssignableFrom(typeof(TImplementation)))
            {
                _builder.RegisterEntryPoint<TickableBridge<TImplementation>>(
                    resolver => new TickableBridge<TImplementation>((ITickableEntryPoint)resolver.Resolve<TImplementation>()),
                    Lifetime.Singleton);
            }
        }

        private sealed class Binding : IServiceBinding
        {
            private readonly RegistrationBuilder _registration;

            public Binding(RegistrationBuilder registration) => _registration = registration;

            public IServiceBinding As(Type serviceType)
            {
                _registration.As(serviceType);
                return this;
            }

            public IServiceBinding AsSelf()
            {
                _registration.AsSelf();
                return this;
            }
        }

        private sealed class StartableBridge<TImplementation> : IStartable
        {
            private readonly IStartableEntryPoint _target;

            public StartableBridge(IStartableEntryPoint target) => _target = target;

            public void Start() => _target.Start();
        }

        private sealed class AsyncStartableBridge<TImplementation> : IAsyncStartable
        {
            private readonly IAsyncStartableEntryPoint _target;

            public AsyncStartableBridge(IAsyncStartableEntryPoint target) => _target = target;

            public UniTask StartAsync(CancellationToken cancellation = default)
                => _target.StartAsync(cancellation);
        }

        private sealed class TickableBridge<TImplementation> : ITickable
        {
            private readonly ITickableEntryPoint _target;

            public TickableBridge(ITickableEntryPoint target) => _target = target;

            public void Tick() => _target.Tick();
        }
    }
}
