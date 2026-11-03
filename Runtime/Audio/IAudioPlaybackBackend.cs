using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Audio
{
    public interface IAudioPlaybackBackend
    {
        UniTask PlayAsync(string audioId, string layerId, CancellationToken cancellationToken);
        UniTask PlayOneShotAsync(string audioId, string layerId, CancellationToken cancellationToken);
        UniTask StopAsync(string audioId, CancellationToken cancellationToken);
        UniTask SetVolumeAsync(string layerId, float volume, CancellationToken cancellationToken);
        UniTask FadeAsync(string layerId, float targetVolume, float durationSeconds, CancellationToken cancellationToken);
        UniTask CrossFadeAsync(string audioId, string layerId, float durationSeconds, CancellationToken cancellationToken);
    }
}
