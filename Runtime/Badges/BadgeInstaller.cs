using System;
using Foundation.DI;

namespace Foundation.Badges
{
    public sealed class BadgeInstaller : IFeatureInstaller
    {
        private readonly BadgeCatalog _catalog;

        public BadgeInstaller(BadgeCatalog catalog) => _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

        public void Install(IServiceRegistry registry)
        {
            registry.RegisterInstance(_catalog);
            registry.Register<BadgeService>(ServiceLifetime.Singleton).As<IBadgeService>();
        }
    }
}
