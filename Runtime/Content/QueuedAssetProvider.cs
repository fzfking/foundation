using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Object = UnityEngine.Object;
using UnityEngine;

namespace Foundation.Content
{
    /// <summary>
    /// Wraps an asset provider with two rules:
    /// <list type="bullet">
    /// <item>Queued loads: only a few run at once per lane, so a screen's dozen sprites do not hit the
    /// backend together. The interactive lane (screens, popups) and the background lane (preloads) are
    /// separate, so a preload never delays a load the player waits for.</item>
    /// <item>Shared loads: requests for the same address and type share one load and one result, a failure
    /// included; the next request after a failure starts a new load.</item>
    /// </list>
    /// Everything else goes straight to the inner provider, whose reference counting still decides when an
    /// asset is released. Call this provider from the Unity main thread: its shared-load table is not synchronized.
    /// </summary>
    public sealed class QueuedAssetProvider : IAssetProvider
    {
        public const int DefaultInteractiveConcurrency = 4;
        public const int DefaultBackgroundConcurrency = 2;

        private readonly IAssetProvider _inner;
        private readonly LoadLane _interactive;
        private readonly LoadLane _background;
        private readonly Dictionary<LoadedAssetRegistry.Key, object> _loads = new();
        private bool _disposed;

        public QueuedAssetProvider(
            IAssetProvider inner,
            int interactiveConcurrency = DefaultInteractiveConcurrency,
            int backgroundConcurrency = DefaultBackgroundConcurrency)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (interactiveConcurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(interactiveConcurrency));
            if (backgroundConcurrency < 1)
                throw new ArgumentOutOfRangeException(nameof(backgroundConcurrency));

            _interactive = new(interactiveConcurrency);
            _background = new(backgroundConcurrency);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _inner.Dispose();
        }

        public UniTask<IAssetHandle<T>> LoadAssetAsync<T>(string assetId, CancellationToken cancellationToken = default)
            where T : Object
            => LoadSharedAsync<T>(assetId, _interactive, cancellationToken);

        public UniTask<IAssetInstance> InstantiateAsync(
            string assetId,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return _inner.InstantiateAsync(assetId, parent, cancellationToken);
        }

        public bool ContainsAsset<T>(string assetId)
            where T : Object
            => !_disposed && _inner.ContainsAsset<T>(assetId);

        public UniTask<IPreloadHandle> PreloadAsync(
            IReadOnlyList<string> assetIds,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            => PreloadInLaneAsync<Object>(assetIds, _interactive, progress, cancellationToken);

        /// <summary>
        /// Loads assets of type <typeparamref name="T"/> ahead of time through the background lane, so what
        /// the player is looking at loads first. Ask for the type the game will request later: the inner
        /// provider keeps the type an address was first loaded as. The handle keeps the assets loaded until
        /// disposed.
        /// </summary>
        public UniTask<IPreloadHandle> PreloadInBackgroundAsync<T>(
            IReadOnlyList<string> assetIds,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
            where T : Object
            => PreloadInLaneAsync<T>(assetIds, _background, progress, cancellationToken);

        private async UniTask<IAssetHandle<T>> LoadSharedAsync<T>(string assetId, LoadLane lane, CancellationToken cancellationToken)
            where T : Object
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var key = new LoadedAssetRegistry.Key(assetId, typeof(T));
            if (_loads.TryGetValue(key, out var stored))
            {
                var shared = (SharedLoad<T>)stored;
                shared.Holders++;
                try
                {
                    await shared.Settled.Task.AttachExternalCancellation(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Drop(key, shared);
                    throw;
                }

                if (shared.Failure != null)
                {
                    Drop(key, shared);
                    throw shared.Failure;
                }

                if (shared.Abandoned)
                {
                    // The first caller gave up before the load started: this request becomes the new first one.
                    Drop(key, shared);
                    return await LoadSharedAsync<T>(assetId, lane, cancellationToken);
                }

                return new AssetHandle<T>(shared.Asset, () => Drop(key, shared));
            }

            var created = new SharedLoad<T> { Holders = 1 };
            _loads.Add(key, created);
            try
            {
                await lane.EnterAsync(cancellationToken);
                try
                {
                    created.Handle = await _inner.LoadAssetAsync<T>(assetId, cancellationToken);
                    created.Asset = created.Handle.Asset;
                }
                finally
                {
                    lane.Exit();
                }
            }
            catch (OperationCanceledException)
            {
                created.Abandoned = true;
                Forget(key, created);
                created.Settled.TrySetResult();
                Drop(key, created);
                throw;
            }
            catch (Exception exception)
            {
                created.Failure = exception;
                Forget(key, created);
                created.Settled.TrySetResult();
                Drop(key, created);
                throw;
            }

            created.Settled.TrySetResult();
            return new AssetHandle<T>(created.Asset, () => Drop(key, created));
        }

        private async UniTask<IPreloadHandle> PreloadInLaneAsync<T>(
            IReadOnlyList<string> assetIds,
            LoadLane lane,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            where T : Object
        {
            if (assetIds == null)
                throw new ArgumentNullException(nameof(assetIds));
            ThrowIfDisposed();

            var count = assetIds.Count;
            var handles = new IAssetHandle<T>[count];
            var failures = new Exception[count];
            var finished = new int[1];
            var tasks = new UniTask[count];
            for (var index = 0; index < count; index++)
                tasks[index] = LoadPreloadItemAsync(assetIds[index], lane, handles, failures, index, finished, count, progress, cancellationToken);
            try
            {
                await UniTask.WhenAll(tasks);

                foreach (var failure in failures)
                    if (failure != null)
                        throw failure;

                var loaded = new List<IAssetHandle<Object>>(count);
                foreach (var handle in handles)
                    loaded.Add(handle);
                return new PreloadHandle(loaded);
            }
            catch (Exception failure)
            {
                try
                {
                    DisposeHandles(handles);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Preload failed and one or more asset handles could not be released.", failure, cleanupFailure);
                }

                throw;
            }
        }

        private async UniTask LoadPreloadItemAsync<T>(
            string assetId,
            LoadLane lane,
            IAssetHandle<T>[] handles,
            Exception[] failures,
            int index,
            int[] finished,
            int count,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            where T : Object
        {
            try
            {
                handles[index] = await LoadSharedAsync<T>(assetId, lane, cancellationToken);
            }
            catch (Exception exception)
            {
                failures[index] = exception;
            }

            var completed = Interlocked.Increment(ref finished[0]);
            try
            {
                progress?.Report((float)completed / count);
            }
            catch (Exception exception)
            {
                failures[index] ??= exception;
            }
        }

        private static void DisposeHandles<T>(IAssetHandle<T>[] handles)
            where T : Object
        {
            List<Exception> failures = null;
            foreach (var handle in handles)
            {
                try
                {
                    handle?.Dispose();
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures != null)
                throw new AggregateException("One or more preloaded asset handles could not be released.", failures);
        }

        private void Drop<T>(LoadedAssetRegistry.Key key, SharedLoad<T> load)
            where T : Object
        {
            load.Holders--;
            if (load.Holders > 0)
                return;

            Forget(key, load);
            load.Handle?.Dispose();
            load.Handle = null;
        }

        // Late requests must not join a load that has already failed or been abandoned.
        private void Forget<T>(LoadedAssetRegistry.Key key, SharedLoad<T> load)
            where T : Object
        {
            if (_loads.TryGetValue(key, out var current) && ReferenceEquals(current, load))
                _loads.Remove(key);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(QueuedAssetProvider));
        }

        private sealed class SharedLoad<T>
            where T : Object
        {
            public IAssetHandle<T> Handle;
            public T Asset;
            public Exception Failure;
            public bool Abandoned;

            // The first caller, every waiter and every handle handed out each count once, until they let go.
            public int Holders;

            public readonly UniTaskCompletionSource Settled = new();
        }

        private sealed class LoadLane
        {
            private readonly int _limit;
            private readonly object _gate = new();
            private readonly List<UniTaskCompletionSource> _waiting = new();
            private int _running;

            public LoadLane(int limit)
            {
                _limit = limit;
            }

            public async UniTask EnterAsync(CancellationToken cancellationToken)
            {
                UniTaskCompletionSource turn;
                lock (_gate)
                {
                    if (_running < _limit)
                    {
                        _running++;
                        return;
                    }

                    turn = new UniTaskCompletionSource();
                    _waiting.Add(turn);
                }

                await using var registration = cancellationToken.Register(() =>
                {
                    lock (_gate)
                        _waiting.Remove(turn);

                    turn.TrySetCanceled(cancellationToken);
                });
                await turn.Task;
            }

            public void Exit()
            {
                UniTaskCompletionSource next;
                lock (_gate)
                {
                    _running--;
                    next = TakeNextLocked();
                }

                while (next != null)
                {
                    if (next.TrySetResult())
                        return;

                    lock (_gate)
                    {
                        _running--;
                        next = TakeNextLocked();
                    }
                }
            }

            private UniTaskCompletionSource TakeNextLocked()
            {
                while (_waiting.Count > 0)
                {
                    var next = _waiting[0];
                    _waiting.RemoveAt(0);
                    _running++;
                    return next;
                }

                return null;
            }
        }
    }
}
