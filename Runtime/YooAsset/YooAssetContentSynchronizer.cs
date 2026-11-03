using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.ContentMirroring;
using Foundation.Logging;
using YooAsset;
using CoreLogger = Foundation.Logging.IAppLogger;

namespace Foundation.Infrastructure.YooAsset
{
    /// <summary>
    /// Offline-first synchronization of a YooAsset package. A version is considered complete when YooAsset reports
    /// that none of its bundles need downloading.
    /// - A device that already has a version starts on it without a network, then moves to a newer remote version
    /// only when the host is reachable and all of the new files have been downloaded. A failed update keeps the
    /// previous version active.
    /// - A device that has none waits for a connection, retrying until it gets the whole package.
    /// </summary>
    public sealed class YooAssetContentSynchronizer
    {
        private const int RequestTimeoutSeconds = 60;
        private const int DownloadConcurrency = 10;
        private const int DownloadRetryCount = 3;
        private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(3);

        private readonly IYooAssetVersionStore _versionStore;
        private readonly ContentMirrorCoordinator _mirrorCoordinator;
        private readonly Func<CancellationToken, UniTask<bool>> _isHostReachable;
        private readonly TimeSpan _retryInterval;
        private readonly CoreLogger _logger;

        public SynchronizationState State { get; private set; } = SynchronizationState.NotStarted;

        public event Action<SynchronizationState> StateChanged;

        /// <param name="isHostReachable">Answers whether the content host can be reached now; it only gates the attempt.</param>
        /// <param name="logger">Receives the warnings about failed requests. Optional: without one, the Foundation Unity log output is used.</param>
        public YooAssetContentSynchronizer(
            IYooAssetVersionStore versionStore,
            Func<CancellationToken, UniTask<bool>> isHostReachable,
            TimeSpan? retryInterval = null,
            CoreLogger logger = null,
            ContentMirrorCoordinator mirrorCoordinator = null)
        {
            _versionStore = versionStore ?? throw new ArgumentNullException(nameof(versionStore));
            _mirrorCoordinator = mirrorCoordinator;
            _isHostReachable = isHostReachable ?? throw new ArgumentNullException(nameof(isHostReachable));
            _retryInterval = retryInterval ?? DefaultRetryInterval;
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        /// <param name="beforeRemoteUpdate">
        /// Runs once, before loading a manifest or contacting the host; returning
        /// false stops with <see cref="ContentUpdateStoppedException"/>. The release check goes here, so a client
        /// that must update never downloads content it cannot play.
        /// </param>
        /// <param name="applyMirror">Receives each selected mirror, or null after a failed mirror pass to restore the primary URL.</param>
        public async UniTask SynchronizeAsync(
            ResourcePackage package,
            IYooAssetRemoteContentSettings settings,
            IProgress<float> progress = null,
            Func<CancellationToken, UniTask<bool>> beforeRemoteUpdate = null,
            CancellationToken cancellationToken = default,
            Action<ContentMirror> applyMirror = null)
        {
            if (package == null)
                throw new ArgumentNullException(nameof(package));
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            if (_mirrorCoordinator != null && applyMirror != null)
            {
                await SynchronizeWithMirrorsAsync(
                    package,
                    settings,
                    progress,
                    beforeRemoteUpdate,
                    applyMirror,
                    cancellationToken);
                return;
            }

            SetState(SynchronizationState.Checking);
            var previous = package.PackageValid ? package.GetPackageVersion() : null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (beforeRemoteUpdate != null && !await beforeRemoteUpdate(cancellationToken).AttachExternalCancellation(cancellationToken))
                    throw new ContentUpdateStoppedException();
                cancellationToken.ThrowIfCancellationRequested();

                var current = _versionStore.Read(package.PackageName);
                if (!await TryUseLocalVersionAsync(package, current, cancellationToken))
                {
                    current = settings.BuiltinPackageVersion;
                    if (!await TryUseLocalVersionAsync(package, current, cancellationToken))
                        current = null;
                }

                if (current != null)
                {
                    if (await _isHostReachable(cancellationToken).AttachExternalCancellation(cancellationToken))
                        await TryUpdateAsync(package, current, progress, cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();
                    SetState(SynchronizationState.Succeeded);
                    return;
                }

                while (!await _isHostReachable(cancellationToken).AttachExternalCancellation(cancellationToken)
                    || !await TryUpdateAsync(package, null, progress, cancellationToken))
                {
                    await WaitForReachabilityAsync(cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
                SetState(SynchronizationState.Succeeded);
            }
            catch (OperationCanceledException)
            {
                if (previous != null && package.PackageValid && package.GetPackageVersion() != previous
                    && !await TryLoadManifestAsync(package, previous, CancellationToken.None))
                    throw new InvalidOperationException($"Could not restore the previous manifest '{previous}' for package '{package.PackageName}'.");
                throw;
            }
        }

        private async UniTask<bool> TryUseLocalVersionAsync(ResourcePackage package, string version, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(version))
                return false;

            var manifest = await TryPrefetchManifestAsync(package, version, cancellationToken);
            if (manifest == null || manifest.CreateResourceDownloader(
                    new ResourceDownloaderOptions(DownloadConcurrency, DownloadRetryCount)).TotalDownloadCount != 0)
                return false;

            return await TryLoadManifestAsync(package, version, cancellationToken);
        }

        private async UniTask<bool> TryUpdateAsync(
            ResourcePackage package,
            string current,
            IProgress<float> progress,
            CancellationToken cancellationToken,
            IProgress<ContentMirrorProgress> mirrorProgress = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(SynchronizationState.Checking);
            var versionOperation = package.RequestPackageVersionAsync(
                new RequestPackageVersionOptions(true, RequestTimeoutSeconds));
            await UniTask.WaitUntil(() => versionOperation.IsDone);
            cancellationToken.ThrowIfCancellationRequested();
            if (versionOperation.Status != EOperationStatus.Succeeded)
            {
                _logger.Log(LogLevel.Warning, "Content", $"Package version request failed: {versionOperation.Error}");
                return false;
            }

            var remote = versionOperation.PackageVersion;
            if (remote == current)
                return true;

            var manifest = await TryPrefetchManifestAsync(package, remote, cancellationToken);
            if (manifest == null)
                return false;

            SetState(SynchronizationState.Downloading);
            var downloader = manifest.CreateResourceDownloader(
                new ResourceDownloaderOptions(DownloadConcurrency, DownloadRetryCount));
            downloader.StartDownload();
            try
            {
                while (!downloader.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(downloader.Progress);
                    mirrorProgress?.Report(new ContentMirrorProgress(
                        downloader.CurrentDownloadBytes,
                        downloader.TotalDownloadBytes));
                    await UniTask.Yield();
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                if (!downloader.IsDone)
                {
                    downloader.CancelDownload();
                    await UniTask.WaitUntil(() => downloader.IsDone);
                }
            }

            if (downloader.Status != EOperationStatus.Succeeded)
            {
                _logger.Log(LogLevel.Warning, "Content", $"Package download failed: {downloader.Error}");
                return false;
            }

            if (!await TryLoadManifestAsync(package, remote, cancellationToken))
                return false;
            progress?.Report(1f);
            mirrorProgress?.Report(new ContentMirrorProgress(
                downloader.CurrentDownloadBytes,
                downloader.TotalDownloadBytes));
            _versionStore.Write(package.PackageName, remote);
            return true;
        }

        private async UniTask SynchronizeWithMirrorsAsync(
            ResourcePackage package,
            IYooAssetRemoteContentSettings settings,
            IProgress<float> progress,
            Func<CancellationToken, UniTask<bool>> beforeRemoteUpdate,
            Action<ContentMirror> applyMirror,
            CancellationToken cancellationToken)
        {
            var previous = package.PackageValid ? package.GetPackageVersion() : null;
            var hasLocalVersion = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (beforeRemoteUpdate != null
                    && !await beforeRemoteUpdate(cancellationToken).AttachExternalCancellation(cancellationToken))
                    throw new ContentUpdateStoppedException();

                while (true)
                {
                    var succeeded = await _mirrorCoordinator.ExecuteAsync(async (mirror, mirrorProgress, attemptToken) =>
                    {
                        applyMirror(mirror);
                        var current = _versionStore.Read(package.PackageName);
                        if (!await TryUseLocalVersionAsync(package, current, attemptToken))
                        {
                            current = settings.BuiltinPackageVersion;
                            if (!await TryUseLocalVersionAsync(package, current, attemptToken))
                                current = null;
                        }

                        hasLocalVersion |= current != null;
                        return await TryUpdateAsync(package, current, progress, attemptToken, mirrorProgress);
                    }, cancellationToken);

                    if (!succeeded)
                        applyMirror(null);

                    if (succeeded || hasLocalVersion)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SetState(SynchronizationState.Succeeded);
                        return;
                    }

                    SetState(SynchronizationState.WaitingForConnection);
                    await UniTask.Delay(_retryInterval, ignoreTimeScale: true, cancellationToken: cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                if (previous != null && package.PackageValid && package.GetPackageVersion() != previous
                    && !await TryLoadManifestAsync(package, previous, CancellationToken.None))
                    throw new InvalidOperationException($"Could not restore the previous manifest '{previous}' for package '{package.PackageName}'.");
                throw;
            }
        }

        private async UniTask<PrefetchManifestOperation> TryPrefetchManifestAsync(
            ResourcePackage package, string version, CancellationToken cancellationToken)
        {
            var operation = package.PrefetchManifestAsync(new PrefetchManifestOptions(version, RequestTimeoutSeconds));
            await UniTask.WaitUntil(() => operation.IsDone);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Status == EOperationStatus.Succeeded)
                return operation;

            _logger.Log(LogLevel.Warning, "Content", $"Package manifest '{version}' failed to load: {operation.Error}");
            return null;
        }

        private async UniTask<bool> TryLoadManifestAsync(
            ResourcePackage package,
            string version,
            CancellationToken cancellationToken)
        {
            var operation = package.LoadPackageManifestAsync(
                new LoadPackageManifestOptions(version, RequestTimeoutSeconds));
            await UniTask.WaitUntil(() => operation.IsDone);
            cancellationToken.ThrowIfCancellationRequested();
            if (operation.Status == EOperationStatus.Succeeded)
                return true;

            _logger.Log(LogLevel.Warning, "Content", $"Package manifest '{version}' failed to load: {operation.Error}");
            return false;
        }

        private async UniTask WaitForReachabilityAsync(CancellationToken cancellationToken)
        {
            SetState(SynchronizationState.WaitingForConnection);
            do
            {
                await UniTask.Delay(_retryInterval, ignoreTimeScale: true, cancellationToken: cancellationToken);
            }
            while (!await _isHostReachable(cancellationToken).AttachExternalCancellation(cancellationToken));
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
