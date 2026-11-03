using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Content;
using Foundation.Spawning;
using CoreLogger = Foundation.Logging.IAppLogger;
using CoreLogLevel = Foundation.Logging.LogLevel;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace Foundation.Infrastructure.YooAsset
{
    public sealed class YooAssetProvider : IAssetProvider
    {
        private readonly YooAssetPackageContext _packageContext;
        private readonly CoreLogger _logger;
        private readonly LoadedAssetRegistry _registry;
        private readonly IObjectSpawner _spawner;
        private bool _disposed;

        public YooAssetProvider(YooAssetPackageContext packageContext, CoreLogger logger, IObjectSpawner spawner = null)
        {
            _packageContext = packageContext ?? throw new ArgumentNullException(nameof(packageContext));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _registry = new();
            _spawner = spawner ?? new AsyncObjectSpawner();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            var entries = _registry.GetAllEntries();
            foreach (var entry in entries)
            {
                if (entry.Handle is AssetHandle handle)
                    ReleaseHandle(handle);
            }

            _registry.Clear();
        }

        public bool ContainsAsset<T>(string assetId)
            where T : Object
        {
            if (_disposed || string.IsNullOrWhiteSpace(assetId) || !_packageContext.IsInitialized)
                return false;

            // YooAsset's manifest records addresses, not asset types (AssetInfo.AssetType only echoes the type asked
            // for), so only the address can be checked here; a wrong type surfaces when the asset is loaded.
            return _packageContext.Package.IsLocationValid(assetId);
        }

        public async UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
            string assetId,
            CancellationToken cancellationToken = default)
            where T : Object
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(YooAssetProvider));

            cancellationToken.ThrowIfCancellationRequested();

            var key = new LoadedAssetRegistry.Key(assetId, typeof(T));
            var info = await GetOrLoadAssetInfoAsync<T>(key, cancellationToken);

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
                throw new ObjectDisposedException(nameof(YooAssetProvider));

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
                _logger.Log(CoreLogLevel.Error, "YooAssetProvider",
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
                throw new ObjectDisposedException(nameof(YooAssetProvider));

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
            CancellationToken cancellationToken)
            where T : Object
        {
            if (_registry.TryGetLoaded(key, out var existingEntry))
            {
                _registry.AddRef(key);
                return existingEntry;
            }

            var completion = _registry.JoinPending(key, out var created);
            if (created)
                LoadSharedAsync<T>(key, completion).Forget();

            return await WaitForLoadAsync(key, completion, cancellationToken);
        }

        // Runs without any caller's token: the load serves every caller waiting on it, so one caller leaving must not
        // cancel it for the others. The completion ends with the registered entry, or with the failure.
        private async UniTaskVoid LoadSharedAsync<T>(LoadedAssetRegistry.Key key, UniTaskCompletionSource completion)
            where T : Object
        {
            AssetHandle handle = null;

            try
            {
                handle = _packageContext.Package.LoadAssetAsync<T>(key.Address);
                // YooAsset drops its handles when the package is destroyed (Play mode stopping), and this wait can still
                // be resumed by the editor loop after that. A dropped handle has nothing left to serve, so the load ends quietly.
                await UniTask.WaitUntil(() => !handle.IsValid || handle.IsDone);
                if (!handle.IsValid)
                {
                    _registry.FailPending(key, completion);
                    completion.TrySetCanceled();
                    return;
                }

                if (handle.Status != EOperationStatus.Succeeded)
                    throw new InvalidOperationException($"Failed to load asset '{key.Address}': {handle.Error}");

                var loadedAsset = handle.AssetObject;
                if (loadedAsset == null)
                    throw new InvalidOperationException($"Loaded asset is null for '{key.Address}'.");

                var entry = new LoadedAssetRegistry.Entry
                {
                    Asset = loadedAsset,
                    Handle = handle,
                };

                // Nothing is registered when every caller left while the load ran, so the asset is released at once.
                if (!_registry.CompletePending(key, completion, entry))
                    ReleaseHandle(handle);

                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                ReleaseHandle(handle);
                _registry.FailPending(key, completion);
                _logger.Log(CoreLogLevel.Error, "YooAssetProvider", $"Failed to load asset '{key.Address}': {exception.Message}");
                completion.TrySetException(exception);
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
                if (releasable?.Handle is AssetHandle handle)
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
            if (remaining == 0 && info.Handle is AssetHandle handle)
                ReleaseHandle(handle);
        }

        private static void ReleaseHandle(AssetHandle handle)
        {
            if (handle == null || !handle.IsValid)
                return;

            handle.Release();
        }
    }
}
