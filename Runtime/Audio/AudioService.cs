using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using System;

namespace Foundation.Audio
{
    /// <summary>
    /// The audio entry point for gameplay: play events by id, never clips or sources. The string-based
    /// members at the bottom are the older API for content addressed by asset id.
    /// </summary>
    public interface IAudioService
    {
        /// <summary>Plays an event. The handle is invalid when the request was rejected (cooldown, playback limit...).</summary>
        AudioHandle Play(AudioEventId id);

        AudioHandle Play(AudioEventId id, in AudioPlayOptions options);

        /// <summary>Plays an event and reports why it did not start. Rejection is normal, not an error.</summary>
        AudioPlayResult TryPlay(AudioEventId id, in AudioPlayOptions options, out AudioHandle handle);

        void Stop(AudioEventId id, float fadeSeconds = 0f);

        /// <summary>Stops every playback and the track of a channel.</summary>
        void StopChannel(AudioChannelId channel, float fadeSeconds = 0f);

        /// <summary>Plays an event as its channel's persistent track, replacing the current one.</summary>
        AudioPlayResult PlayMusic(AudioEventId id, float fadeInSeconds = 0f);

        AudioPlayResult CrossFadeMusic(AudioEventId id, float durationSeconds);

        void StopMusic(AudioChannelId channel, float fadeOutSeconds = 0f);

        /// <summary>Makes the clips of the events resident so their first play is not missed.</summary>
        /// <remarks>Propagates asset-loading failures; cancellation is also propagated.</remarks>
        UniTask PreloadAsync(IReadOnlyList<AudioEventId> ids, CancellationToken cancellationToken = default);

        UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken = default);
        UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken = default);
        UniTask StopAsync(string audioId, CancellationToken cancellationToken = default);
        UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken = default);
        UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken = default);
        UniTask CrossFadeAsync(string audioId, string layerId, float durationSeconds, CancellationToken cancellationToken = default);
    }

    public sealed class AudioService : IAudioService
    {
        private readonly IAudioPlaybackBackend _backend;
        private readonly AudioEngine _engine;

        public AudioService(IAudioPlaybackBackend backend, AudioEngine engine)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public AudioHandle Play(AudioEventId id) => _engine.Play(id);

        public AudioHandle Play(AudioEventId id, in AudioPlayOptions options) => _engine.Play(id, in options);

        public AudioPlayResult TryPlay(AudioEventId id, in AudioPlayOptions options, out AudioHandle handle) => _engine.TryPlay(id, in options, out handle);

        public void Stop(AudioEventId id, float fadeSeconds = 0f) => _engine.StopEvent(id, fadeSeconds);

        public void StopChannel(AudioChannelId channel, float fadeSeconds = 0f) => _engine.StopChannel(channel, fadeSeconds);

        public AudioPlayResult PlayMusic(AudioEventId id, float fadeInSeconds = 0f) => _engine.PlayMusic(id, fadeInSeconds);

        public AudioPlayResult CrossFadeMusic(AudioEventId id, float durationSeconds) => _engine.CrossFadeMusic(id, durationSeconds);

        public void StopMusic(AudioChannelId channel, float fadeOutSeconds = 0f) => _engine.StopMusic(channel, fadeOutSeconds);

        public UniTask PreloadAsync(IReadOnlyList<AudioEventId> ids, CancellationToken cancellationToken = default) => _engine.PreloadAsync(ids, cancellationToken);

        public UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken = default)
            => _backend.PlayAsync(Require(audioId, nameof(audioId)), Require(layerId, nameof(layerId)), cancellationToken);

        public UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken = default)
            => _backend.PlayOneShotAsync(Require(audioId, nameof(audioId)), Require(layerId, nameof(layerId)), cancellationToken);

        public UniTask StopAsync(string audioId, CancellationToken cancellationToken = default)
            => _backend.StopAsync(Require(audioId, nameof(audioId)), cancellationToken);

        public UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken = default)
            => _backend.SetVolumeAsync(Require(layerId, nameof(layerId)), volume, cancellationToken);

        public UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken = default)
            => _backend.FadeAsync(Require(layerId, nameof(layerId)), targetVolume, durationSeconds, cancellationToken);

        public UniTask CrossFadeAsync(
            string audioId,
            string layerId,
            float durationSeconds,
            CancellationToken cancellationToken = default)
            => _backend.CrossFadeAsync(
                Require(audioId, nameof(audioId)),
                Require(layerId, nameof(layerId)),
                durationSeconds,
                cancellationToken);

        private static string Require(string value, string name)
            => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Audio identifier is required.", name) : value;
    }
}
