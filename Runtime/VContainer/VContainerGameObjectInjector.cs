using System;
using Foundation.DI;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Foundation.Infrastructure.VContainer
{
    /// <summary>
    /// Injects a GameObject with VContainer (its <c>[Inject]</c> members), then hands the scope's resolver to
    /// every <see cref="IServiceResolverClient"/> under it, which is how Foundation's own components get
    /// their services.
    /// </summary>
    public sealed class VContainerGameObjectInjector : IGameObjectInjector
    {
        private readonly IObjectResolver _resolver;
        private readonly IServiceResolver _serviceResolver;

        public VContainerGameObjectInjector(IObjectResolver resolver, IServiceResolver serviceResolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _serviceResolver = serviceResolver ?? throw new ArgumentNullException(nameof(serviceResolver));
        }

        public void Inject(GameObject instance)
        {
            if (instance == null)
                return;

            _resolver.InjectGameObject(instance);
            var clients = instance.GetComponentsInChildren<IServiceResolverClient>(true);
            foreach (var serviceResolverClient in clients)
                serviceResolverClient.Bind(_serviceResolver);
        }
    }
}
