using System;
using Foundation.DI;
using VContainer;

namespace Foundation.Infrastructure.VContainer
{
    /// <summary>
    /// <see cref="IServiceScope"/> over a VContainer <see cref="IObjectResolver"/>. A scope made by
    /// <see cref="CreateScope"/> owns and disposes the container beneath it; the view a registered service
    /// receives does not.
    /// </summary>
    public sealed class VContainerServiceScope : IServiceScope
    {
        private readonly IObjectResolver _resolver;
        private readonly bool _ownsScope;

        public IObjectResolver Resolver => _resolver;

        public VContainerServiceScope(IObjectResolver resolver, bool ownsScope)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _ownsScope = ownsScope;
        }

        public void Dispose()
        {
            if (_ownsScope)
                _resolver.Dispose();
        }

        public object Resolve(Type serviceType) => _resolver.Resolve(serviceType);

        public bool TryResolve(Type serviceType, out object service) => _resolver.TryResolve(serviceType, out service);

        public IServiceScope CreateScope(Action<IServiceRegistry> configure = null)
        {
            var child = _resolver.CreateScope(builder => configure?.Invoke(new VContainerServiceRegistry(builder)));
            return new VContainerServiceScope(child, ownsScope: true);
        }
    }
}
