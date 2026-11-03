using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Logging;
using Foundation.ContentMirroring;
using Cysharp.Threading.Tasks;
using CoreLogger = Foundation.Logging.IAppLogger;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceProviders;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Offline-first content synchronization. The unit is the whole content of the active catalog: a device
    /// has all of it or it is not in sync, so it never plays on a catalog with parts missing once the
    /// connection is gone.
    /// - <see cref="SynchronizeForStartupAsync"/>: picks up a newer remote catalog when online, then
    /// downloads everything missing. Returns once all of it is on the device; a device that has it all starts
    /// offline.
    /// - <see cref="EnsureLabelAsync"/>: a no-op after startup sync, kept as a safety net that downloads
    /// missing content or waits for a connection.
    /// Network failures never throw: the state is <see cref="SynchronizationState.WaitingForConnection"/> and
    /// it keeps retrying.
    /// </summary>
    public sealed class AddressablesContentSynchronizer
    {
        private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(3);

        // Tests substitute the Unity signal; production reads Application directly.
        internal static Func<NetworkReachability> DeviceNetworkReachabilityProvider { get; set; }
            = () => Application.internetReachability;

        private readonly IContentHostReachabilityChecker _reachabilityChecker;
        private readonly ContentMirrorCoordinator _mirrorCoordinator;
        private readonly TimeSpan _retryInterval;
        private readonly CoreLogger _logger;

        public SynchronizationState State { get; private set; } = SynchronizationState.NotStarted;

        public event Action<SynchronizationState> StateChanged;

        /// <param name="logger">Receives the warnings about failed checks and downloads. Optional: without one, the Foundation Unity log output is used.</param>
        public AddressablesContentSynchronizer(
            IContentHostReachabilityChecker reachabilityChecker,
            TimeSpan? retryInterval = null,
            CoreLogger logger = null,
            ContentMirrorCoordinator mirrorCoordinator = null)
        {
            _reachabilityChecker = reachabilityChecker ?? throw new ArgumentNullException(nameof(reachabilityChecker));
            _mirrorCoordinator = mirrorCoordinator;
            _retryInterval = retryInterval ?? DefaultRetryInterval;
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        public async UniTask SynchronizeForStartupAsync(
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_mirrorCoordinator != null)
            {
                await EnsureWithMirrorsAsync(CollectAllContentLocations, progress, true, cancellationToken);
                return;
            }

            SetState(SynchronizationState.Checking);
            if (await _reachabilityChecker.IsReachableAsync(cancellationToken))
                await TryUpdateCatalogsAsync(cancellationToken);

            await EnsureAsync(CollectAllContentLocations, progress, cancellationToken);
        }

        public UniTask EnsureLabelAsync(
            string label,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_mirrorCoordinator != null)
                return EnsureLabelWithMirrorsAsync(label, progress, cancellationToken);

            return EnsureAsync(() => CollectLabelLocations(label), progress, cancellationToken);
        }

        private async UniTask EnsureLabelWithMirrorsAsync(
            string label,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            var locations = CollectLabelLocations(label);
            if (await GetDownloadSizeAsync(locations, cancellationToken) == 0)
            {
                SetState(SynchronizationState.Succeeded);
                return;
            }

            await EnsureWithMirrorsAsync(() => CollectLabelLocations(label), progress, false, cancellationToken);
        }

        private async UniTask EnsureWithMirrorsAsync(
            Func<List<IResourceLocation>> collect,
            IProgress<float> progress,
            bool updateCatalogs,
            CancellationToken cancellationToken)
        {
            var catalogChecked = !updateCatalogs;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var localDownloadSize = await GetDownloadSizeAsync(collect(), cancellationToken);
                // Mirror mode gates only on the device network state: a failed host probe is unknown, not offline.
                if (IsNetworkUnavailable())
                {
                    if (localDownloadSize == 0)
                    {
                        SetState(SynchronizationState.Succeeded);
                        return;
                    }

                    SetState(SynchronizationState.WaitingForConnection);
                    await UniTask.Delay(_retryInterval, ignoreTimeScale: true, cancellationToken: cancellationToken);
                    continue;
                }

                SetState(SynchronizationState.Checking);

                if (!catalogChecked)
                {
                    catalogChecked = true;
                    await UpdateCatalogsOnceAsync(cancellationToken);
                }

                var succeeded = await _mirrorCoordinator.ExecuteAsync(async (mirror, mirrorProgress, attemptToken) =>
                {
                    AddressablesEndpointRouting.ApplyMirror(mirror);
                    return await EnsureAttemptAsync(collect, progress, mirrorProgress, attemptToken, cancellationToken);
                },
                    cancellationToken,
                    Math.Max(0, localDownloadSize));

                if (succeeded)
                {
                    SetState(SynchronizationState.Succeeded);
                    return;
                }

                AddressablesEndpointRouting.RestoreEndpoint();
                SetState(SynchronizationState.WaitingForConnection);
                await UniTask.Delay(_retryInterval, ignoreTimeScale: true, cancellationToken: cancellationToken);
            }
        }

        // Addressables leaves its ResourceManager throwing on every update after a failed catalog check or update, so the
        // update runs at most once, on the release endpoint rather than a mirror, and only when that endpoint serves the
        // catalog files. Mirrors then carry only bundles, where a single failed attempt is isolated and the next one runs.
        private async UniTask UpdateCatalogsOnceAsync(CancellationToken cancellationToken)
        {
            AddressablesEndpointRouting.RestoreEndpoint();
            try
            {
                if (!await RemoteCatalogReadiness.IsReadyAsync(cancellationToken))
                {
                    _logger.Log(LogLevel.Warning, "Content",
                        "Skipped the catalog update: the release endpoint does not serve the catalog files.");
                    return;
                }

                await TryUpdateCatalogsAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.Log(LogLevel.Warning, "Content", $"Catalog update failed: {exception.Message}");
            }
        }

        private bool IsNetworkUnavailable()
            => _reachabilityChecker is INetworkReachabilityStatusProvider provider
                ? provider.IsNetworkUnavailable
                : DeviceNetworkReachabilityProvider() == NetworkReachability.NotReachable;

        private async UniTask<bool> EnsureAttemptAsync(
            Func<List<IResourceLocation>> collect,
            IProgress<float> progress,
            IProgress<ContentMirrorProgress> mirrorProgress,
            CancellationToken cancellationToken,
            CancellationToken callerCancellationToken)
        {
            SetState(SynchronizationState.Checking);
            var locations = collect();
            var downloadSize = await GetDownloadSizeAsync(
                locations,
                cancellationToken,
                callerCancellationToken,
                waitForAttemptCompletion: true);
            if (downloadSize == 0)
                return true;

            return await TryDownloadAsync(
                locations,
                progress,
                mirrorProgress,
                cancellationToken,
                callerCancellationToken,
                waitForAttemptCompletion: true);
        }

        private async UniTask EnsureAsync(
            Func<List<IResourceLocation>> collect,
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetState(SynchronizationState.Checking);

                var locations = collect();
                var downloadSize = await GetDownloadSizeAsync(locations, cancellationToken);
                if (downloadSize == 0)
                {
                    SetState(SynchronizationState.Succeeded);
                    return;
                }

                if (await _reachabilityChecker.IsReachableAsync(cancellationToken)
                    && await TryDownloadAsync(locations, progress, cancellationToken))
                {
                    SetState(SynchronizationState.Succeeded);
                    return;
                }

                await WaitForReachabilityAsync(cancellationToken);
            }
        }

        private async UniTask<bool> TryUpdateCatalogsAsync(CancellationToken cancellationToken)
        {
            foreach (var provider in AddressablesApi.ResourceManager.ResourceProviders)
                if (provider is ContentCatalogProvider catalogProvider)
                    catalogProvider.DisableCatalogUpdateOnStart = false;

            List<string> catalogs = null;
            var check = AddressablesApi.CheckForCatalogUpdates(false);
            try
            {
                await WaitAsync(check, cancellationToken);
                if (check.Status == AsyncOperationStatus.Succeeded)
                    catalogs = check.Result;
                else
                {
                    _logger.Log(LogLevel.Warning, "Content", $"Catalog update check failed: {check.OperationException?.Message}");
                    return false;
                }
            }
            finally
            {
                check.Release();
            }

            if (catalogs == null || catalogs.Count == 0)
                return true;

            var update = AddressablesApi.UpdateCatalogs(catalogs, false);
            try
            {
                await WaitAsync(update, cancellationToken);
                if (update.Status != AsyncOperationStatus.Succeeded)
                {
                    _logger.Log(LogLevel.Warning, "Content", $"Catalog update failed: {update.OperationException?.Message}");
                    return false;
                }
            }
            finally
            {
                update.Release();
            }

            return true;
        }

        private static List<IResourceLocation> CollectAllContentLocations()
        {
            var result = new List<IResourceLocation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var locator in AddressablesApi.ResourceLocators)
            {
                foreach (var key in locator.Keys)
                {
                    if (!locator.Locate(key, null, out var locations))
                        continue;
                    foreach (var location in locations)
                    {
                        if (location.HasDependencies && seen.Add(LocationKey(location)))
                            result.Add(location);
                    }
                }
            }
            return result;
        }

        private static List<IResourceLocation> CollectLabelLocations(string label)
        {
            var result = new List<IResourceLocation>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var locator in AddressablesApi.ResourceLocators)
                if (locator.Locate(label, null, out var locations))
                    foreach (var location in locations)
                        if (location.HasDependencies && seen.Add(LocationKey(location)))
                            result.Add(location);
            return result;
        }

        private static string LocationKey(IResourceLocation location)
            => location.InternalId + "|" + location.ResourceType?.FullName;

        private async UniTask<long> GetDownloadSizeAsync(
            List<IResourceLocation> locations,
            CancellationToken cancellationToken,
            CancellationToken callerCancellationToken = default,
            bool waitForAttemptCompletion = false)
        {
            if (locations.Count == 0)
                return 0;

            var handle = AddressablesApi.GetDownloadSizeAsync(new object[] { locations });
            try
            {
                await WaitAsync(handle, cancellationToken, callerCancellationToken, waitForAttemptCompletion);
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    return handle.Result;
                _logger.Log(LogLevel.Warning, "Content", $"Content download size check failed: {handle.OperationException?.Message}");
                return -1;
            }
            finally
            {
                handle.Release();
            }
        }

        private async UniTask<bool> TryDownloadAsync(
            List<IResourceLocation> locations,
            IProgress<float> progress,
            CancellationToken cancellationToken)
            => await TryDownloadAsync(locations, progress, null, cancellationToken, default, false);

        private async UniTask<bool> TryDownloadAsync(
            List<IResourceLocation> locations,
            IProgress<float> progress,
            IProgress<ContentMirrorProgress> mirrorProgress,
            CancellationToken cancellationToken,
            CancellationToken callerCancellationToken,
            bool waitForAttemptCompletion)
        {
            SetState(SynchronizationState.Downloading);
            var handle = AddressablesApi.DownloadDependenciesAsync(locations, false);
            try
            {
                while (!handle.IsDone)
                {
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    catch (OperationCanceledException) when (waitForAttemptCompletion && !callerCancellationToken.IsCancellationRequested)
                    {
                        while (!handle.IsDone)
                        {
                            callerCancellationToken.ThrowIfCancellationRequested();
                            await UniTask.Yield();
                        }

                        throw;
                    }

                    var status = handle.GetDownloadStatus();
                    progress?.Report(status.Percent);
                    mirrorProgress?.Report(new ContentMirrorProgress(status.DownloadedBytes, status.TotalBytes));
                    await UniTask.Yield();
                }

                progress?.Report(1f);
                var finalStatus = handle.GetDownloadStatus();
                mirrorProgress?.Report(new ContentMirrorProgress(finalStatus.DownloadedBytes, finalStatus.TotalBytes));
                if (handle.Status == AsyncOperationStatus.Succeeded)
                    return true;
                _logger.Log(LogLevel.Warning, "Content", $"Content download failed: {handle.OperationException?.Message}");
                return false;
            }
            finally
            {
                handle.Release();
            }
        }

        private static async UniTask WaitAsync<T>(
            AsyncOperationHandle<T> handle,
            CancellationToken cancellationToken,
            CancellationToken callerCancellationToken = default,
            bool waitForAttemptCompletion = false)
        {
            try
            {
                while (!handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }
            }
            catch (OperationCanceledException) when (waitForAttemptCompletion && !callerCancellationToken.IsCancellationRequested)
            {
                while (!handle.IsDone)
                {
                    callerCancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }

                throw;
            }
        }

        private async UniTask WaitForReachabilityAsync(CancellationToken cancellationToken)
        {
            SetState(SynchronizationState.WaitingForConnection);
            do
            {
                await UniTask.Delay(_retryInterval, ignoreTimeScale: true, cancellationToken: cancellationToken);
            }
            while (!await _reachabilityChecker.IsReachableAsync(cancellationToken));
        }

        private void SetState(SynchronizationState state)
        {
            if (State == state)
                return;

            State = state;
            StateChanged?.Invoke(state);
        }

        public enum SynchronizationState
        {
            NotStarted,
            Checking,
            Downloading,
            WaitingForConnection,
            Succeeded,
        }
    }
}
