using System;
using Foundation.DI;

namespace Foundation.GameModules
{
    /// <summary>
    /// Adapts a loaded <see cref="IGameModule"/> into an <see cref="IFeatureInstaller"/>, so a runtime-loaded
    /// module installs through the same call as every compile-time feature.
    /// </summary>
    public sealed class GameModuleFeatureInstaller : IFeatureInstaller
    {
        private readonly IGameModule _module;

        public GameModuleFeatureInstaller(IGameModule module)
        {
            _module = module ?? throw new ArgumentNullException(nameof(module));
        }

        public void Install(IServiceRegistry registry) => _module.Configure(registry);
    }
}
