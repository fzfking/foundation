using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Foundation.Content;
using Foundation.Logging;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// Keeps the clips that events reference by asset id resident, loaded through
    /// <see cref="IAssetProvider"/>. Each clip is loaded once and held until <see cref="Unload"/> or disposal; the
    /// cache owns the handles. Direct clip references bypass it.
    /// </summary>
    public sealed class AudioClipCache : IAudioClipResolver, IDisposable
    {
        private readonly IAssetProvider _assets;
        private readonly IAppLogger _logger;
        private readonly IAudioClock _clock;
        private readonly double _retryDelaySeconds;
        private readonly Dictionary<string, IAssetHandle<AudioClip>> _loaded = new(StringComparer.Ordinal);
        private readonly Dictionary<string, UniTaskCompletionSource<Exception>> _inFlight = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _retryAt = new(StringComparer.Ordinal);
        private readonly CancellationTokenSource _lifetime = new();
        private bool _disposed;

        public int LoadedCount => _loaded.Count;

        /// <param name="retryDelaySeconds">
        /// Delay before a playback request may retry a clip whose load failed. Zero or less keeps a failed clip
        /// failed until an explicit <see cref="PreloadAsync"/>; a positive value enables automatic retries.
        /// </param>
        public AudioClipCache(
            IAssetProvider assets,
            IAppLogger logger = null,
            double retryDelaySeconds = 0d,
            IAudioClock clock = null)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            _retryDelaySeconds = retryDelaySeconds > 0d ? retryDelaySeconds : 0d;
            _clock = clock ?? UnscaledAudioClock.Instance;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _lifetime.Cancel();
            foreach (var handle in _loaded.Values)
                handle.Dispose();

            _loaded.Clear();
            _lifetime.Dispose();
        }

        public bool TryGetClip(in AudioClipEntry entry, out AudioClip clip)
        {
            clip = entry.Clip;
            if (clip != null)
                return true;

            var assetId = entry.AssetId;
            if (string.IsNullOrEmpty(assetId) || _disposed)
                return false;

            if (_loaded.TryGetValue(assetId, out var handle))
            {
                clip = handle.Asset;
                return clip != null;
            }

            // Not resident: start loading so the next request succeeds. This one is rejected, never delayed.
            if (_retryAt.TryGetValue(assetId, out var retryAt) && _clock.Now < retryAt)
                return false;

            LoadAsync(assetId).Forget();
            return false;
        }

        public async UniTask PreloadAsync(IReadOnlyList<AudioClipEntry> entries, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pending = new List<UniTask<Exception>>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < entries.Count; index++)
            {
                var assetId = entries[index].AssetId;
                if (entries[index].Clip == null && !string.IsNullOrEmpty(assetId) && seen.Add(assetId))
                    pending.Add(LoadAsync(assetId));
            }

            if (pending.Count == 0)
                return;

            var failures = await UniTask.WhenAll(pending).AttachExternalCancellation(cancellationToken);
            foreach (var failure in failures)
            {
                if (failure != null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        public void Unload(string assetId)
        {
            if (_loaded.Remove(assetId, out var handle))
            {
                handle.Dispose();
            }
        }

        private UniTask<Exception> LoadAsync(string assetId)
        {
            if (_loaded.ContainsKey(assetId))
                return UniTask.FromResult<Exception>(null);

            if (_inFlight.TryGetValue(assetId, out var running))
                return running.Task;

            // A completion source can be awaited by any number of callers, unlike the UniTask of one running load.
            var source = new UniTaskCompletionSource<Exception>();
            _inFlight[assetId] = source;
            RunLoadAsync(assetId, source).Forget();
            return source.Task;
        }

        private async UniTaskVoid RunLoadAsync(string assetId, UniTaskCompletionSource<Exception> source)
        {
            Exception failure = null;
            try
            {
                var handle = await _assets.LoadAssetAsync<AudioClip>(assetId, _lifetime.Token);
                if (_disposed)
                {
                    handle.Dispose();
                    failure = new OperationCanceledException();
                    return;
                }

                _retryAt.Remove(assetId);
                _loaded[assetId] = handle;
            }
            catch (OperationCanceledException exception)
            {
                failure = exception;
            }
            catch (Exception exception)
            {
                _retryAt[assetId] = _retryDelaySeconds > 0d
                    ? _clock.Now + _retryDelaySeconds
                    : double.PositiveInfinity;
                failure = exception;
                _logger.Log(LogLevel.Warning, "Audio", $"clip '{assetId}' could not be loaded: {exception.Message}");
            }
            finally
            {
                _inFlight.Remove(assetId);
                source.TrySetResult(failure);
            }
        }
    }
}
