using System;
using Foundation.DI;
using Foundation.GameContexts;
using VContainer;

namespace Foundation.Infrastructure.VContainer
{
    /// <summary>
    /// Builds a context's scope as a VContainer child of the application scope. Child registrations shadow
    /// the parent's, so a context gets its own config repository, save orchestrator or injector while still
    /// resolving application services from above. Relies on two VContainer behaviours: a
    /// <c>Lifetime.Singleton</c> in a child is owned and disposed by it, and entry points registered in a
    /// child are dispatched at build and unhooked from the player loop on disposal.
    /// </summary>
    public sealed class VContainerGameContextScopeFactory : IGameContextScopeFactory
    {
        private readonly IObjectResolver _applicationScope;

        public VContainerGameContextScopeFactory(IObjectResolver applicationScope)
        {
            _applicationScope = applicationScope ?? throw new ArgumentNullException(nameof(applicationScope));
        }

        public IServiceScope CreateScope(GameContextDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            var child = _applicationScope.CreateScope(builder =>
            {
                var registry = new VContainerServiceRegistry(builder);
                registry.RegisterInstance(definition);
                registry.RegisterInstance(definition.Save);

                foreach (var installer in definition.Installers)
                    installer.Install(registry);
            });

            return new VContainerServiceScope(child, ownsScope: true);
        }
    }
}
