using System;
using System.Threading;
using Foundation.Content;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// The string-id playback API (clips by asset id, channels by name) on top of <see cref="AudioEngine"/>,
    /// so it shares playbacks, limits, volumes and ducking with event playback. Persistent playback uses the
    /// channel's track; one-shots use pooled playbacks and hold their asset handle until the sound ends.
    /// </summary>
    public sealed class UnityAudioPlaybackBackend : IAudioPlaybackBackend
    {
        private readonly IAssetProvider _assets;
        private readonly AudioEngine _engine;
        private readonly IAudioSettings _settings;
        private readonly CancellationTokenSource _lifetime = new();

        public UnityAudioPlaybackBackend(IAssetProvider assets, AudioEngine engine, IAudioSettings settings)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken)
        {
            var channel = RequireChannel(layerId);
            var handle = await _assets.LoadAssetAsync<AudioClip>(audioId, cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine.PlayTrack(new AudioChannelId(layerId), handle.Asset, audioId, handle, channel.Loop);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public async UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken)
        {
            RequireChannel(layerId);
            var handle = await _assets.LoadAssetAsync<AudioClip>(audioId, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                handle.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
            }

            // The engine takes the handle: it is released when the sound ends, or at once if the playback is refused.
            _engine.TryPlayClip(handle.Asset, new AudioChannelId(layerId), handle, out _);
        }

        public UniTask StopAsync(string audioId, CancellationToken cancellationToken)
        {
            _engine.StopTracks(audioId);
            return UniTask.CompletedTask;
        }

        public UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken)
        {
            RequireChannel(layerId);
            _settings.SetChannelVolume(layerId, volume);
            return UniTask.CompletedTask;
        }

        public async UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken)
        {
            RequireChannel(layerId);
            var duration = Mathf.Max(0f, durationSeconds);
            _engine.FadeChannel(new AudioChannelId(layerId), targetVolume, duration);
            if (duration > 0f)
                await WaitAsync(duration, cancellationToken);
        }

        public async UniTask CrossFadeAsync(string audioId, string layerId, float durationSeconds, CancellationToken cancellationToken)
        {
            var channel = RequireChannel(layerId);
            var handle = await _assets.LoadAssetAsync<AudioClip>(audioId, cancellationToken);
            var duration = Mathf.Max(0f, durationSeconds);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine.CrossFadeTrack(new AudioChannelId(layerId), handle.Asset, audioId, handle, channel.Loop, duration);
            }
            catch
            {
                handle.Dispose();
                throw;
            }

            if (duration > 0f)
                await WaitAsync(duration, cancellationToken);
        }

        private IAudioChannelDefinition RequireChannel(string layerId)
            => _engine.TryGetChannel(layerId, out var channel)
                ? channel
                : throw new InvalidOperationException($"Audio channel '{layerId}' is not registered.");

        private UniTask WaitAsync(float seconds, CancellationToken cancellationToken)
            => UniTask.Delay(TimeSpan.FromSeconds(seconds), DelayType.UnscaledDeltaTime, PlayerLoopTiming.Update, cancellationToken);
    }
}
