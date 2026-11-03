using System.Collections.Generic;
using Foundation.DI;
using Foundation.Lifecycle;
using VContainer;
using VContainer.Unity;

namespace Foundation.Infrastructure.VContainer
{
    /// <summary>
    /// The application's VContainer root: applies the project's feature installers and starts its
    /// <see cref="RootEntryPoint"/>. A project derives one scene component from it.
    /// </summary>
    public abstract class VContainerRootScope<TEntryPoint> : LifetimeScope where TEntryPoint : RootEntryPoint
    {
        protected abstract IReadOnlyList<IFeatureInstaller> FeatureInstallers { get; }

        protected override void Configure(IContainerBuilder builder)
        {
            var registry = new VContainerServiceRegistry(builder);
            PrebindActions(registry);
            InstallFeatures(registry);
            registry.RegisterEntryPoint<TEntryPoint>();
        }

        /// <summary>
        /// Registrations that must exist before the feature installers run (scene references, UI root,
        /// logging). Override this, or the <see cref="IContainerBuilder"/> overload for VContainer-style
        /// code.
        /// </summary>
        protected virtual void PrebindActions(IServiceRegistry registry) => PrebindActions(registry.ToContainerBuilder());

        protected virtual void PrebindActions(IContainerBuilder builder)
        {
        }

        private void InstallFeatures(IServiceRegistry registry)
        {
            foreach (var featureInstaller in FeatureInstallers)
                featureInstaller.Install(registry);
        }
    }
}
