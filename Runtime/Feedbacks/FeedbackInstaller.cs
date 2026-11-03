using System;
using Foundation.Audio;
using Foundation.DI;
using Foundation.Haptics;
using Foundation.Logging;
using Foundation.Motion;

namespace Foundation.Feedbacks
{
    public sealed class FeedbackInstaller : IFeatureInstaller
    {
        private readonly FeedbackLibrary _library;

        public FeedbackInstaller(FeedbackLibrary library) => _library = library ?? throw new ArgumentNullException(nameof(library));

        public void Install(IServiceRegistry registry)
        {
            registry.RegisterInstance(_library);
            registry.Register(
                resolver => new FeedbackService(
                    _library,
                    resolver.TryResolve(out IAudioService audio) ? audio : null,
                    resolver.TryResolve(out IHapticsService haptics) ? haptics : null,
                    resolver.TryResolve(out IMotionService motion) ? motion : null,
                    resolver.TryResolve(out IAppLogger logger) ? logger : null),
                ServiceLifetime.Singleton).As<IFeedbackService>();
        }
    }
}
