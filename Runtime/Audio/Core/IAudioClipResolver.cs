using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// Turns a clip entry into a playable clip without blocking. A direct reference is returned as is; an
    /// asset id once its asset is resident, and is requested in the background otherwise.
    /// </summary>
    public interface IAudioClipResolver
    {
        bool TryGetClip(in AudioClipEntry entry, out AudioClip clip);

        /// <summary>Loads referenced clips and propagates load failures and cancellation.</summary>
        UniTask PreloadAsync(IReadOnlyList<AudioClipEntry> entries, CancellationToken cancellationToken = default);
    }

    /// <summary>Resolves only direct references. For tests and projects that keep all clips in libraries.</summary>
    public sealed class DirectAudioClipResolver : IAudioClipResolver
    {
        public bool TryGetClip(in AudioClipEntry entry, out AudioClip clip)
        {
            clip = entry.Clip;
            return clip != null;
        }

        public UniTask PreloadAsync(IReadOnlyList<AudioClipEntry> entries, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
    }
}
