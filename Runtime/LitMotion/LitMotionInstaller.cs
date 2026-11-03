using Foundation.DI;
using Foundation.Motion;

namespace Foundation.Infrastructure.LitMotion
{
    public sealed class LitMotionInstaller : IFeatureInstaller
    {
        public void Install(IServiceRegistry registry)
        {
            registry.RegisterInstance<IMotionService>(new LitMotionService());
        }
    }
}
