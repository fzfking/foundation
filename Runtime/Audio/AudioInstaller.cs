using System;
using System.Collections.Generic;
using Foundation.Content;
using Foundation.DI;
using Foundation.Lifecycle;
using Foundation.Logging;
using Foundation.Randomization;

namespace Foundation.Audio
{
    /// <summary>
    /// Registers the audio system: settings, engine, pooled Unity backend, clip cache, event registry and
    /// <see cref="IAudioService"/>. Install <c>PoolingInstaller</c> first. Libraries are added to
    /// <see cref="AudioEventRegistry"/> by the project when their content loads.
    /// </summary>
    public sealed class AudioInstaller : IFeatureInstaller
    {
        private readonly IReadOnlyList<IAudioChannelDefinition> _channels;
        private readonly IReadOnlyList<AudioDuckingRule> _ducking;
        private readonly AudioEngineSettings _engineSettings;
        private readonly IAudioMixerRouting _routing;
        private readonly double _clipRetryDelaySeconds;

        public AudioInstaller(
            IReadOnlyList<IAudioChannelDefinition> channels,
            IReadOnlyList<AudioDuckingRule> ducking = null,
            AudioEngineSettings engineSettings = null,
            IAudioMixerRouting routing = null,
            double clipRetryDelaySeconds = 0d)
        {
            _channels = channels ?? throw new ArgumentNullException(nameof(channels));
            _ducking = ducking;
            _engineSettings = engineSettings;
            _routing = routing ?? NullAudioMixerRouting.Instance;
            _clipRetryDelaySeconds = clipRetryDelaySeconds > 0d ? clipRetryDelaySeconds : 0d;
        }

        public void Install(IServiceRegistry registry)
        {
            registry.RegisterInstance(_channels);
            registry.RegisterInstance(_routing).As<IAudioMixerRouting>();
            registry.Register<AudioSettingsState>(ServiceLifetime.Singleton).As<IAudioSettings>();
            registry.Register<UnityAudioBackend>(ServiceLifetime.Singleton).As<IAudioBackend>();
            registry.Register(
                resolver => new AudioClipCache(
                    resolver.Resolve<IAssetProvider>(),
                    resolver.TryResolve(out IAppLogger logger) ? logger : null,
                    _clipRetryDelaySeconds),
                ServiceLifetime.Singleton).As<IAudioClipResolver>();
            registry.Register<AudioEventRegistry>(ServiceLifetime.Singleton);
            registry.Register(
                resolver => new AudioEngine(
                    resolver.Resolve<IAudioBackend>(),
                    _channels,
                    resolver.Resolve<IAudioSettings>(),
                    resolver.Resolve<AudioEventRegistry>(),
                    resolver.Resolve<IAudioClipResolver>(),
                    resolver.TryResolve(out IRandomService random) ? random : new SystemRandomService(),
                    null,
                    _ducking,
                    _engineSettings),
                ServiceLifetime.Singleton);
            registry.RegisterEntryPoint<AudioEngineTicker>();
            registry.Register<UnityAudioPlaybackBackend>(ServiceLifetime.Singleton).As<IAudioPlaybackBackend>();
            registry.Register<AudioService>(ServiceLifetime.Singleton).As<IAudioService>();
        }
    }

    public sealed class AudioEngineTicker : ITickableEntryPoint
    {
        private readonly AudioEngine _engine;

        public AudioEngineTicker(AudioEngine engine)
        {
            _engine = engine;
        }

        public void Tick() => _engine.Tick();
    }
}
