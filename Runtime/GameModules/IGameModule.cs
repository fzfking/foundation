using Foundation.DI;

namespace Foundation.GameModules
{
    /// <summary>
    /// One loadable unit of gameplay composition, handed back by an <see cref="IGameModuleLoader"/>. Its
    /// shape mirrors <see cref="Foundation.DI.IFeatureInstaller"/>: a module composes into a DI scope like a
    /// compile-time installer (see <see cref="GameModuleFeatureInstaller"/>). Nothing assumes HybridCLR or
    /// remote delivery; a module can be a compile-time type registered with
    /// <see cref="BuiltInGameModuleLoader"/>.
    /// </summary>
    public interface IGameModule
    {
        GameModuleId Id { get; }

        void Configure(IServiceRegistry registry);
    }
}
