using System;
using Foundation.DI;
using Foundation.GameContexts;

namespace Foundation.Tests.Support
{
    public sealed class TestGameContextScopeFactory : IGameContextScopeFactory
    {
        private readonly IServiceScope _applicationScope;

        public TestGameContextScopeFactory(IServiceScope applicationScope)
        {
            _applicationScope = applicationScope ?? throw new ArgumentNullException(nameof(applicationScope));
        }

        public IServiceScope CreateScope(GameContextDefinition definition)
        {
            return _applicationScope.CreateScope(registry =>
            {
                registry.RegisterInstance(definition);
                registry.RegisterInstance(definition.Save);

                foreach (var installer in definition.Installers)
                    installer.Install(registry);
            });
        }
    }
}
