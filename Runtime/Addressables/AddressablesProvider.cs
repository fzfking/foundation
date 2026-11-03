using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Foundation.ContentMirroring;
using Foundation.Spawning;
using CoreLogger = Foundation.Logging.IAppLogger;
using CoreLogLevel = Foundation.Logging.LogLevel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using Object = UnityEngine.Object;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    public sealed class AddressablesProvider : IAssetProvider
    {
        private readonly CoreLogger _logger;
        private readonly LoadedAssetRegistry _registry;
        private readonly IObjectSpawner _spawner;
        private readonly ContentMirrorCoordinator _mirrorCoordinator;
        private bool _disposed;

        public AddressablesProvider(
            CoreLogger logger,
            IObjectSpawner spawner = null,
            ContentMirrorCoordinator mirrorCoordinator = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = new();
            _spawner = spawner ?? new AsyncObjectSpawner();
            _mirrorCoordinator = mirrorCoordinator;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            var entries = _registry.GetAllEntries();
            foreach (var entry in entries)
            {
                if (entry.Handle is AsyncOperationHandle handle)
                    ReleaseHandle(handle);
            }

            _registry.Clear();
        }

        public bool ContainsAsset<T>(string assetId)
            where T : Object
        {
            if (_disposed || string.IsNullOrWhiteSpace(assetId))
                return false;

            foreach (var locator in AddressablesApi.ResourceLocators)
            {
                if (locator.Locate(assetId, typeof(T), out _))
                    return true;
            }

            return false;
        }

        public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
            string assetId,
            CancellationToken cancellationToken = default)
            where T : Object
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesProvider));

            cancellationToken.ThrowIfCancellationRequested();

            var key = new LoadedAssetRegistry.Key(assetId, typeof(T));
            var allowMirrorFallback = _mirrorCoordinator != null
                && !_mirrorCoordinator.IsActiveAttemptToken(cancellationToken);
            var info = await GetOrLoadAssetInfoAsync<T>(key, cancellationToken, allowMirrorFallback);

            if (info.Asset is not T typedAsset)
            {
                ReleaseRef(key, info);
                throw new InvalidOperationException(
                    $"Asset '{assetId}' is not of type {typeof(T).Name}. "
                    + $"Actual type: {info.Asset?.GetType().Name ?? "null"}.");
            }

            return new AssetHandle<T>(typedAsset, () => ReleaseRef(key, info));
        }

        public async UniTask<IAssetInstance> InstantiateAsync(
            string assetId,
            Transform parent = null,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesProvider));

            cancellationToken.ThrowIfCancellationRequested();

            var prefab = await LoadAssetAsync<GameObject>(assetId, cancellationToken);
            try
            {
                var instance = await _spawner.InstantiateAsync(prefab.Asset, parent, cancellationToken);
                return new AssetInstance(instance, () => prefab.Dispose());
            }
            catch (OperationCanceledException)
            {
                prefab.Dispose();
                throw;
            }
            catch (Exception exception)
            {
                _logger.Log(CoreLogLevel.Error, "AddressablesProvider",
                    $"Failed to instantiate asset '{assetId}': {exception.Message}");
                prefab.Dispose();
                throw;
            }
        }

        public async UniTask<IPreloadHandle> PreloadAsync(
            IReadOnlyList<string> assetIds,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (assetIds == null)
                throw new ArgumentNullException(nameof(assetIds));
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesProvider));

            var handles = new List<IAssetHandle<Object>>(assetIds.Count);

            try
            {
                for (var index = 0; index < assetIds.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    handles.Add(await LoadAssetAsync<Object>(assetIds[index], cancellationToken));
                    progress?.Report((float)(index + 1) / assetIds.Count);
                }
            }
            catch
            {
                foreach (var handle in handles)
                    handle.Dispose();
                throw;
            }

            return new PreloadHandle(handles);
        }

        private async UniTask<LoadedAssetRegistry.Entry> GetOrLoadAssetInfoAsync<T>(
            LoadedAssetRegistry.Key key,
            CancellationToken cancellationToken,
            bool allowMirrorFallback)
            where T : Object
        {
            if (_registry.TryGetLoaded(key, out var existingEntry))
            {
                _registry.AddRef(key);
                return existingEntry;
            }

            var completion = _registry.JoinPending(key, out var created);
            if (created)
                LoadSharedAsync<T>(key, completion, allowMirrorFallback).Forget();

            return await WaitForLoadAsync(key, completion, cancellationToken);
        }

        // Runs without any caller's token: the load serves every caller waiting on it, so one caller leaving must not
        // cancel it for the others. The completion ends with the registered entry, or with the failure.
        private async UniTaskVoid LoadSharedAsync<T>(
            LoadedAssetRegistry.Key key,
            UniTaskCompletionSource completion,
            bool allowMirrorFallback)
            where T : Object
        {
            var handle = default(AsyncOperationHandle<T>);
            var handleAcquired = false;

            try
            {
                handle = await LoadAssetHandleWithMirrorFallbackAsync<T>(key.Address, allowMirrorFallback);
                handleAcquired = handle.IsValid();
                // The Addressables runtime drops its handles when Play mode stops, and this wait can still be resumed by
                // the editor loop after that. A dropped handle has nothing left to serve, so the load ends quietly.
                if (!handle.IsValid())
                {
                    _registry.FailPending(key, completion);
                    completion.TrySetCanceled();
                    return;
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException($"Failed to load asset '{key.Address}': {handle.OperationException}");

                var loadedAsset = handle.Result;
                if (loadedAsset == null)
                    throw new InvalidOperationException($"Loaded asset is null for '{key.Address}'.");

                var entry = new LoadedAssetRegistry.Entry
                {
                    Asset = loadedAsset,
                    // Stored as the non-generic handle so `entry.Handle is AsyncOperationHandle` matches later.
                    Handle = (AsyncOperationHandle)handle,
                };

                // Nothing is registered when every caller left while the load ran, so the asset is released at once.
                if (!_registry.CompletePending(key, completion, entry))
                    ReleaseHandle(handle);

                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                if (handleAcquired)
                    ReleaseHandle(handle);

                _registry.FailPending(key, completion);
                _logger.Log(CoreLogLevel.Error, "AddressablesProvider", $"Failed to load asset '{key.Address}': {exception.Message}");
                completion.TrySetException(exception);
            }
        }

        private async UniTask<AsyncOperationHandle<T>> LoadAssetHandleWithMirrorFallbackAsync<T>(
            string address,
            bool allowMirrorFallback)
            where T : Object
        {
            var failedMirrorBaseUrl = AddressablesEndpointRouting.CurrentMirrorBaseUrl;
            AsyncOperationHandle<T> handle;
            Exception lastFailure;
            try
            {
                handle = await LoadAssetHandleAsync<T>(address);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                if (_mirrorCoordinator == null || !allowMirrorFallback)
                    throw;

                lastFailure = exception;
                return await LoadFromOtherMirrorsAsync<T>(address, failedMirrorBaseUrl, 0, lastFailure);
            }

            if (!handle.IsValid() || (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null))
                return handle;

            lastFailure = handle.OperationException
                ?? new InvalidOperationException($"Addressables returned no asset for '{address}'.");
            ReleaseHandle(handle);
            if (_mirrorCoordinator == null || !allowMirrorFallback)
                throw lastFailure;

            var expectedDownloadBytes = 0L;
            try
            {
                expectedDownloadBytes = Math.Max(0, await AddressablesAssetCache.GetDownloadSizeAsync(address));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.Log(CoreLogLevel.Debug, "AddressablesProvider",
                    $"Could not estimate the download size for '{address}' before mirror failover: {exception.Message}");
            }

            return await LoadFromOtherMirrorsAsync<T>(address, failedMirrorBaseUrl, expectedDownloadBytes, lastFailure);
        }

        private async UniTask<AsyncOperationHandle<T>> LoadFromOtherMirrorsAsync<T>(
            string address,
            string failedMirrorBaseUrl,
            long expectedDownloadBytes,
            Exception initialFailure)
            where T : Object
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesProvider), initialFailure.Message);

            _logger.Log(CoreLogLevel.Warning, "AddressablesProvider",
                $"Asset '{address}' failed on '{failedMirrorBaseUrl ?? "the current content route"}'; trying other mirrors.");

            var lastFailure = initialFailure;
            var successfulHandle = default(AsyncOperationHandle<T>);
            var succeeded = await _mirrorCoordinator.ExecuteAsync(async (mirror, _, attemptToken) =>
            {
                try
                {
                    AddressablesEndpointRouting.ApplyMirror(mirror);
                    await AddressablesAssetCache.ClearDependenciesAsync(address, attemptToken);
                    var attemptHandle = await LoadAssetHandleAsync<T>(address, attemptToken);
                    if (!attemptHandle.IsValid())
                    {
                        lastFailure = new InvalidOperationException(
                            $"Addressables invalidated the load of '{address}' on mirror '{mirror.Id}'.");
                        return false;
                    }

                    if (attemptHandle.Status != AsyncOperationStatus.Succeeded || attemptHandle.Result == null)
                    {
                        lastFailure = attemptHandle.OperationException
                            ?? new InvalidOperationException($"Addressables returned no asset for '{address}' on mirror '{mirror.Id}'.");
                        ReleaseHandle(attemptHandle);
                        _logger.Log(CoreLogLevel.Warning, "AddressablesProvider",
                            $"Asset '{address}' failed on mirror '{mirror.Id}': {lastFailure.Message}");
                        return false;
                    }

                    successfulHandle = attemptHandle;
                    _logger.Log(CoreLogLevel.Info, "AddressablesProvider",
                        $"Loaded asset '{address}' from fallback mirror '{mirror.Id}'.");
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                    _logger.Log(CoreLogLevel.Warning, "AddressablesProvider",
                        $"Asset '{address}' failed on mirror '{mirror.Id}': {exception.Message}");
                    return false;
                }
            },
                expectedDownloadBytes: expectedDownloadBytes,
                excludedMirrorBaseUrl: failedMirrorBaseUrl);

            if (succeeded)
                return successfulHandle;

            AddressablesEndpointRouting.RestoreEndpoint();
            throw new InvalidOperationException(
                $"Could not load asset '{address}' from the current route or any other configured mirror.",
                lastFailure);
        }

        private static async UniTask<AsyncOperationHandle<T>> LoadAssetHandleAsync<T>(
            string address,
            CancellationToken cancellationToken = default)
            where T : Object
        {
            var handle = AddressablesApi.LoadAssetAsync<T>(address);
            try
            {
                while (handle.IsValid() && !handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }

                return handle;
            }
            catch
            {
                ReleaseHandle(handle);
                throw;
            }
        }

        private async UniTask<LoadedAssetRegistry.Entry> WaitForLoadAsync(
            LoadedAssetRegistry.Key key,
            UniTaskCompletionSource completion,
            CancellationToken cancellationToken)
        {
            try
            {
                await completion.Task.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // This caller leaves: its reserved reference goes back, and the asset is released if that was the last one.
                var releasable = _registry.AbandonReservation(key, completion);
                if (releasable?.Handle is AsyncOperationHandle handle)
                    ReleaseHandle(handle);
                throw;
            }

            // The reference reserved when this caller joined is the one the entry holds for it now.
            if (_registry.TryGetLoaded(key, out var entry))
                return entry;

            throw new InvalidOperationException($"Asset '{key.Address}' was not registered after its load completed.");
        }

        private void ReleaseRef(LoadedAssetRegistry.Key key, LoadedAssetRegistry.Entry info)
        {
            if (_disposed)
                return;

            var remaining = _registry.ReleaseRef(key);
            if (remaining == 0 && info.Handle is AsyncOperationHandle handle)
                ReleaseHandle(handle);
        }

        private static void ReleaseHandle(AsyncOperationHandle handle)
        {
            if (!handle.IsValid())
                return;

            AddressablesApi.Release(handle);
        }
    }
}
