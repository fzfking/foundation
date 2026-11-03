using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;

namespace Foundation.DI
{
    /// <summary>
    /// A deliberately small container behind the DI port, so Foundation runs, and is tested, without a DI
    /// library: constructor injection (the longest constructor, resolved by type), the three lifetimes, child
    /// scopes that shadow their parent, build and dispose callbacks, and entry points that start with the scope
    /// and tick when the host calls <see cref="Tick"/>.
    /// It resolves by reflection, has no cycle detection, no generated factories and no game-object injection.
    /// For a shipping game prefer a maintained container (the VContainer adapter is included) and keep this one
    /// for tests and tools.
    /// <para>
    /// Transient services are created on every resolve and are not tracked for disposal, even when they implement
    /// <see cref="IDisposable"/>: the caller owns them. Singleton and scoped instances the scope creates are disposed
    /// with the scope that owns them.
    /// </para>
    /// <para>
    /// Collection resolutions (<c>IEnumerable&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>,
    /// <c>IReadOnlyCollection&lt;T&gt;</c>, <c>T[]</c>) are not cached: each resolve builds a new array and resolves
    /// every matching registration again. The array is a snapshot, not a live view.
    /// </para>
    /// </summary>
    public sealed class MinimalServiceScope : IServiceScope
    {
        private readonly MinimalServiceScope _parent;
        private readonly Dictionary<Type, Registration> _registrations = new();
        private readonly List<Registration> _allRegistrations = new();
        private readonly CancellationTokenSource _lifetime = new();
        private readonly Dictionary<Registration, object> _instances = new();
        private readonly List<IDisposable> _owned = new();
        private readonly List<Action<IServiceResolver>> _disposeCallbacks = new();
        private readonly List<ITickableEntryPoint> _tickables = new();
        private bool _disposed;
        private bool _disposing;

        public bool IsDisposed => _disposed;

        private MinimalServiceScope(MinimalServiceScope parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            if (_disposed || _disposing)
                return;

            _disposing = true;
            List<Exception> failures = null;
            try
            {
                try
                {
                    _lifetime.Cancel();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }

                _tickables.Clear();
                foreach (var callback in _disposeCallbacks)
                {
                    try
                    {
                        callback(this);
                    }
                    catch (Exception exception)
                    {
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }

                _disposed = true;
                for (var index = _owned.Count - 1; index >= 0; index--)
                {
                    try
                    {
                        _owned[index].Dispose();
                    }
                    catch (Exception exception)
                    {
                        (failures ??= new List<Exception>()).Add(exception);
                    }
                }
            }
            finally
            {
                _disposed = true;
                _disposing = false;
                try
                {
                    _lifetime.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            if (failures != null)
                throw new AggregateException("One or more errors occurred while disposing the service scope.", failures);
        }

        public static MinimalServiceScope Create(Action<IServiceRegistry> configure = null)
        {
            var scope = new MinimalServiceScope(null);
            scope.Build(configure);
            return scope;
        }

        public IServiceScope CreateScope(Action<IServiceRegistry> configure = null)
        {
            ThrowIfDisposed();
            var child = new MinimalServiceScope(this);
            child.Build(configure);
            return child;
        }

        public object Resolve(Type serviceType)
        {
            if (TryResolve(serviceType, out var service))
                return service;

            throw new InvalidOperationException($"No registration for {serviceType.Name}.");
        }

        public bool TryResolve(Type serviceType, out object service)
        {
            ThrowIfDisposed();
            for (var scope = this; scope != null; scope = scope._parent)
            {
                if (scope._registrations.TryGetValue(serviceType, out var registration))
                {
                    service = InstanceOf(registration);
                    return true;
                }
            }

            if (TryResolveCollection(serviceType, out service))
                return true;

            service = null;
            return false;
        }

        public void Tick()
        {
            if (_disposed || _disposing)
                return;

            // By index and with no copy: entry points are only added while the scope is built, and disposal stops the loop.
            for (var index = 0; index < _tickables.Count; index++)
            {
                if (_disposed || _disposing)
                    break;
                _tickables[index].Tick();
            }
        }

        private void Build(Action<IServiceRegistry> configure)
        {
            var registry = new Registry(this);
            var disposeCallbacksCopied = false;
            try
            {
                configure?.Invoke(registry);
                registry.PublishAll();
                _disposeCallbacks.AddRange(registry.DisposeCallbacks);
                disposeCallbacksCopied = true;
                foreach (var callback in registry.BuildCallbacks)
                    callback(this);
                foreach (var entryPoint in registry.EntryPoints)
                {
                    var instance = InstanceOf(entryPoint);
                    if (instance is IStartableEntryPoint startable)
                        startable.Start();
                    if (instance is IAsyncStartableEntryPoint asyncStartable)
                    {
                        // shortcut: synchronous build failure can cancel but cannot await this start; it must honor the scope token.
                        StartEntryPointAsync(asyncStartable, _lifetime.Token).Forget();
                    }
                    if (instance is ITickableEntryPoint tickable)
                        _tickables.Add(tickable);
                }
            }
            catch (Exception buildFailure)
            {
                if (!disposeCallbacksCopied)
                    _disposeCallbacks.AddRange(registry.DisposeCallbacks);

                try
                {
                    Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "Service scope build failed and cleanup also failed.", buildFailure, cleanupFailure);
                }

                throw;
            }
        }

        private object InstanceOf(Registration registration)
        {
            if (registration.Lifetime == ServiceLifetime.Transient)
                return Create(registration);

            // A singleton belongs to the scope that registered it; a scoped service to the scope asking for it.
            var owner = registration.Lifetime == ServiceLifetime.Singleton ? registration.Owner : this;
            if (!owner._instances.TryGetValue(registration, out var instance))
            {
                instance = owner.Create(registration);
                owner._instances.Add(registration, instance);
            }

            return instance;
        }

        private object Create(Registration registration)
        {
            if (registration.Instance != null)
                return registration.Instance;

            var resolver = registration.Lifetime == ServiceLifetime.Singleton ? registration.Owner : this;
            object created;
            if (registration.Factory != null)
            {
                created = registration.Factory(resolver);
            }
            else
            {
                var constructor = registration.ImplementationType.GetConstructors()
                    .OrderByDescending(candidate => candidate.GetParameters().Length)
                    .First();
                var arguments = constructor.GetParameters()
                    .Select(parameter => resolver.TryResolve(parameter.ParameterType, out var dependency)
                        ? dependency
                        : parameter.HasDefaultValue ? parameter.DefaultValue : resolver.Resolve(parameter.ParameterType))
                    .ToArray();
                created = constructor.Invoke(arguments);
            }

            if (created is IDisposable disposable && registration.Lifetime != ServiceLifetime.Transient
                && registration.Instance == null)
                (registration.Lifetime == ServiceLifetime.Singleton ? registration.Owner : this)._owned.Add(disposable);

            return created;
        }

        private static async UniTask StartEntryPointAsync(IAsyncStartableEntryPoint entryPoint, CancellationToken cancellationToken)
        {
            try
            {
                await entryPoint.StartAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }

        private bool TryResolveCollection(Type serviceType, out object service)
        {
            var elementType = serviceType.IsArray ? serviceType.GetElementType() : null;
            if (elementType == null && serviceType.IsGenericType)
            {
                var definition = serviceType.GetGenericTypeDefinition();
                if (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>)
                    || definition == typeof(IReadOnlyCollection<>))
                    elementType = serviceType.GetGenericArguments()[0];
            }

            if (elementType == null)
            {
                service = null;
                return false;
            }

            var registrations = new List<Registration>();
            CollectRegistrations(elementType, registrations);
            var array = Array.CreateInstance(elementType, registrations.Count);
            for (var index = 0; index < registrations.Count; index++)
                array.SetValue(InstanceOf(registrations[index]), index);
            service = array;
            return true;
        }

        private void CollectRegistrations(Type serviceType, List<Registration> result)
        {
            _parent?.CollectRegistrations(serviceType, result);
            foreach (var registration in _allRegistrations)
            {
                var matches = registration.ServiceTypes == null
                    ? registration.ImplementationType == serviceType
                    : registration.ServiceTypes.Contains(serviceType);
                if (matches)
                    result.Add(registration);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MinimalServiceScope));
        }

        private sealed class Registration
        {
            public MinimalServiceScope Owner;
            public Type ImplementationType;
            public Func<IServiceResolver, object> Factory;
            public object Instance;
            public ServiceLifetime Lifetime;
            public List<Type> ServiceTypes;
            public bool IsEntryPoint;
        }

        private sealed class Registry : IServiceRegistry
        {
            private readonly MinimalServiceScope _scope;
            private readonly List<Registration> _all = new();

            public List<Action<IServiceResolver>> BuildCallbacks { get; } = new();

            public List<Action<IServiceResolver>> DisposeCallbacks { get; } = new();

            public IEnumerable<Registration> EntryPoints => _all.Where(registration => registration.IsEntryPoint);

            public Registry(MinimalServiceScope scope)
            {
                _scope = scope;
                var self = new Registration
                {
                    Owner = scope,
                    Instance = scope,
                    ImplementationType = typeof(MinimalServiceScope),
                    Lifetime = ServiceLifetime.Singleton,
                    ServiceTypes = new List<Type> { typeof(IServiceScope), typeof(IServiceResolver) },
                };
                scope._registrations[typeof(IServiceScope)] = self;
                scope._registrations[typeof(IServiceResolver)] = self;
            }

            public IServiceBinding Register(Type implementationType, ServiceLifetime lifetime)
                => Add(new Registration { ImplementationType = implementationType, Lifetime = lifetime });

            public IServiceBinding Register<TImplementation>(ServiceLifetime lifetime)
                => Register(typeof(TImplementation), lifetime);

            public IServiceBinding Register<TService>(Func<IServiceResolver, TService> factory, ServiceLifetime lifetime)
                => Add(new Registration
                {
                    ImplementationType = typeof(TService),
                    Factory = resolver => factory(resolver),
                    Lifetime = lifetime,
                });

            public IServiceBinding RegisterInstance<TService>(TService instance)
                => Add(new Registration
                {
                    ImplementationType = typeof(TService),
                    Instance = instance,
                    Lifetime = ServiceLifetime.Singleton,
                });

            public IServiceBinding RegisterEntryPoint<TImplementation>(ServiceLifetime lifetime = ServiceLifetime.Singleton)
                => AddEntryPoint(new Registration { ImplementationType = typeof(TImplementation), Lifetime = lifetime });

            public IServiceBinding RegisterEntryPoint<TImplementation>(
                Func<IServiceResolver, TImplementation> factory,
                ServiceLifetime lifetime = ServiceLifetime.Singleton)
                => AddEntryPoint(new Registration
                {
                    ImplementationType = typeof(TImplementation),
                    Factory = resolver => factory(resolver),
                    Lifetime = lifetime,
                });

            public void RegisterBuildCallback(Action<IServiceResolver> callback) => BuildCallbacks.Add(callback);

            public void RegisterDisposeCallback(Action<IServiceResolver> callback) => DisposeCallbacks.Add(callback);

            // Published once configuration is done, so As/AsSelf order does not matter; a later registration of the
            // same service wins.
            public void PublishAll()
            {
                _scope._allRegistrations.AddRange(_all);
                foreach (var registration in _all)
                {
                    var types = registration.ServiceTypes ?? new List<Type> { registration.ImplementationType };
                    foreach (var type in types)
                        _scope._registrations[type] = registration;
                }
            }

            private IServiceBinding Add(Registration registration)
            {
                registration.Owner = _scope;
                _all.Add(registration);
                return new Binding(registration);
            }

            private IServiceBinding AddEntryPoint(Registration registration)
            {
                registration.IsEntryPoint = true;
                registration.ServiceTypes = new List<Type>(registration.ImplementationType.GetInterfaces()) { registration.ImplementationType };
                return Add(registration);
            }
        }

        private sealed class Binding : IServiceBinding
        {
            private readonly Registration _registration;

            public Binding(Registration registration) => _registration = registration;

            public IServiceBinding As(Type serviceType)
            {
                // An explicit As drops the default "as its own type" exposure, as the real containers do.
                _registration.ServiceTypes ??= new List<Type>();

                if (!_registration.ServiceTypes.Contains(serviceType))
                    _registration.ServiceTypes.Add(serviceType);

                return this;
            }

            public IServiceBinding AsSelf() => As(_registration.ImplementationType);
        }
    }
}
