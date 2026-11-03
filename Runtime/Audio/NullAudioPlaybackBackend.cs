using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Audio
{
    public sealed class NullAudioPlaybackBackend : IAudioPlaybackBackend
    {
        public UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask StopAsync(string audioId, CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken) => UniTask.CompletedTask;
        public UniTask CrossFadeAsync(string audioId, string layerId, float durationSeconds, CancellationToken cancellationToken) => UniTask.CompletedTask;
    }
}
