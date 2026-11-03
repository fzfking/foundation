using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using YooAsset;

namespace Foundation.Infrastructure.YooAsset
{
    public sealed class YooAssetContentInitializer : IDisposable
    {
        private readonly IYooAssetContentSettings _settings;
        private readonly YooAssetPackageContext _context;
        private readonly YooAssetContentSynchronizer _synchronizer;
        private readonly CancellationTokenSource _disposeCts = new();
        private AsyncLazy<ExceptionDispatchInfo> _initialization;
        private ExceptionDispatchInfo _initializationFailure;
        private ResourcePackage _package;
        private YooAssetRemoteService _remoteService;
        private bool _ownsYooAssets;
        private bool _ownsPackage;
        private bool _initializationInProgress;
        private bool _disposed;

        public YooAssetContentInitializer(
            IYooAssetContentSettings settings,
            YooAssetPackageContext context,
            YooAssetContentSynchronizer synchronizer = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _synchronizer = synchronizer;
            if (UsesOfflineFirst && _synchronizer == null)
                throw new ArgumentException(
                    $"{nameof(ContentStartupStrategy.OfflineFirst)} needs a {nameof(YooAssetContentSynchronizer)}.", nameof(synchronizer));
        }

        private bool UsesOfflineFirst
            => UsesHostMode && ((IYooAssetRemoteContentSettings)_settings).StartupStrategy == ContentStartupStrategy.OfflineFirst;

        private bool UsesHostMode
        {
            get
            {
#if UNITY_EDITOR
                if (_settings.UseEditorSimulation)
                    return false;
#endif
                return !_settings.UseOfflinePlayerMode && _settings is IYooAssetRemoteContentSettings;
            }
        }

        /// <param name="progress">Reports the download of an offline-first package; the other modes have nothing to report.</param>
        /// <param name="beforeRemoteUpdate">Host mode only. Runs before the host is contacted; see <see cref="YooAssetContentSynchronizer.SynchronizeAsync"/>.</param>
        /// <remarks>The first call determines the cancellation, progress and gate of the shared initialization.
        /// Later callers can cancel their own wait without canceling the shared operation.</remarks>
        public UniTask InitializeAsync(
            CancellationToken cancellationToken = default,
            IProgress<float> progress = null,
            Func<CancellationToken, UniTask<bool>> beforeRemoteUpdate = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(YooAssetContentInitializer));
            var isFirstCall = _initialization == null;
            _initialization ??= new AsyncLazy<ExceptionDispatchInfo>(
                () => InitializeSharedAsync(cancellationToken, progress, beforeRemoteUpdate));
            var result = _initialization.Task;
            if (!isFirstCall)
                result = result.AttachExternalCancellation(cancellationToken);

            return new UniTask(new InitializationResultSource(this, result), 0);
        }

        private async UniTask<ExceptionDispatchInfo> InitializeSharedAsync(
            CancellationToken cancellationToken,
            IProgress<float> progress,
            Func<CancellationToken, UniTask<bool>> beforeRemoteUpdate)
        {
            _initializationInProgress = true;
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
                _initializationFailure = await InitializeInternalAsync(linkedCts.Token, progress, beforeRemoteUpdate);
            }
            catch (Exception exception)
            {
                _initializationFailure = ExceptionDispatchInfo.Capture(exception);
            }
            finally
            {
                _initializationInProgress = false;
                if (_disposed)
                    _disposeCts.Dispose();
            }

            return _initializationFailure;
        }

        private async UniTask<ExceptionDispatchInfo> InitializeInternalAsync(
            CancellationToken cancellationToken,
            IProgress<float> progress,
            Func<CancellationToken, UniTask<bool>> beforeRemoteUpdate)
        {
            ResourcePackage package = null;
            var stoppedByGate = false;

            async UniTask<bool> CheckGateAsync(CancellationToken token)
            {
                var allowed = await beforeRemoteUpdate(token).AttachExternalCancellation(token);
                token.ThrowIfCancellationRequested();
                if (!allowed)
                    stoppedByGate = true;
                return allowed;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!YooAssets.IsInitialized)
                {
                    YooAssets.Initialize();
                    _ownsYooAssets = true;
                }

                if (YooAssets.ContainsPackage(_settings.PackageName))
                {
                    package = YooAssets.GetPackage(_settings.PackageName);
                }
                else
                {
                    package = YooAssets.CreatePackage(_settings.PackageName);
                    _ownsPackage = true;
                }

                if (package.InitializeStatus != EOperationStatus.Succeeded)
                {
                    var options = CreateInitializeOptions();
                    var operation = package.InitializePackageAsync(options);
                    await UniTask.WaitUntil(() => operation.IsDone);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (operation.Status != EOperationStatus.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Failed to initialize YooAsset package '{_settings.PackageName}': {operation.Error}");
                    }
                }

                if (UsesOfflineFirst)
                {
                    await _synchronizer.SynchronizeAsync(
                        package,
                        (IYooAssetRemoteContentSettings)_settings,
                        progress,
                        beforeRemoteUpdate == null ? null : CheckGateAsync,
                        cancellationToken,
                        _remoteService?.ApplyMirror);
                    cancellationToken.ThrowIfCancellationRequested();
                    _context.Publish(package);
                    _package = package;
                    return null;
                }

                if (UsesHostMode && beforeRemoteUpdate != null && !await CheckGateAsync(cancellationToken))
                    throw new ContentUpdateStoppedException();

                var versionOperation = package.RequestPackageVersionAsync();
                await UniTask.WaitUntil(() => versionOperation.IsDone);
                cancellationToken.ThrowIfCancellationRequested();
                if (versionOperation.Status != EOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to request YooAsset package version '{_settings.PackageName}': {versionOperation.Error}");
                }

                var manifestOperation = package.LoadPackageManifestAsync(
                    new LoadPackageManifestOptions(versionOperation.PackageVersion, 60));
                await UniTask.WaitUntil(() => manifestOperation.IsDone);
                cancellationToken.ThrowIfCancellationRequested();
                if (manifestOperation.Status != EOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to load YooAsset package manifest '{_settings.PackageName}': {manifestOperation.Error}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                _context.Publish(package);
                _package = package;
                return null;
            }
            catch (OperationCanceledException exception)
            {
                await CleanupPackageAsync(package);
                return ExceptionDispatchInfo.Capture(
                    stoppedByGate && !cancellationToken.IsCancellationRequested
                        ? new ContentUpdateStoppedException()
                        : exception);
            }
            catch (Exception exception)
            {
                await CleanupPackageAsync(package);
                return ExceptionDispatchInfo.Capture(new InvalidOperationException(
                    $"YooAsset content initialization failed for package '{_settings.PackageName}'.", exception));
            }
        }

        private InitializePackageOptions CreateInitializeOptions()
        {
#if UNITY_EDITOR
            if (_settings.UseEditorSimulation)
            {
                var buildResult = EditorSimulateBuildInvoker.Build(
                    _settings.PackageName,
                    (int)EBundleType.VirtualAssetBundle);
                return new EditorSimulateModeOptions
                {
                    EditorFileSystemParameters =
                        FileSystemParameters.CreateDefaultEditorFileSystemParameters(
                            buildResult.PackageRootDirectory)
                };
            }
#endif
            if (_settings.UseOfflinePlayerMode)
            {
                return new OfflinePlayModeOptions
                {
                    BuiltinFileSystemParameters =
                        FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                };
            }

            if (UsesHostMode)
            {
                var settings = (IYooAssetRemoteContentSettings)_settings;
                var builtinParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
                if (UsesOfflineFirst && !string.IsNullOrEmpty(settings.BuiltinPackageVersion))
                    builtinParameters.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);

                return new HostPlayModeOptions
                {
                    BuiltinFileSystemParameters = builtinParameters,
                    CacheFileSystemParameters =
                        FileSystemParameters.CreateDefaultSandboxFileSystemParameters(
                            _remoteService = new YooAssetRemoteService(settings.RemoteUrl))
                };
            }

            throw new PlatformNotSupportedException(
                "No YooAsset player initialization mode is configured for this project.");
        }

        private async UniTask CleanupPackageAsync(ResourcePackage package)
        {
            _context.Clear(_package);
            _package = null;

            if (_ownsPackage && package != null && package.InitializeStatus != EOperationStatus.None)
            {
                var destroyOperation = package.DestroyPackageAsync();
                await UniTask.WaitUntil(() => destroyOperation.IsDone);
            }

            if (_ownsPackage && package != null && YooAssets.IsInitialized && YooAssets.ContainsPackage(package.PackageName))
                YooAssets.RemovePackage(package.PackageName);

            _ownsPackage = false;
            _remoteService = null;

            if (_ownsYooAssets && YooAssets.IsInitialized)
            {
                YooAssets.Destroy();
                _ownsYooAssets = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _disposeCts.Cancel();
            if (_initializationInProgress)
                return;
            _disposeCts.Dispose();
            _context.Clear(_package);
            var package = _package;
            _package = null;

            try
            {
                // Owning YooAssets means owning everything in it: Destroy() synchronously
                // destroys every package and clears the registry, which is precisely the
                // teardown needed and needs no operation to wait on.
                if (_ownsYooAssets && YooAssets.IsInitialized)
                {
                    YooAssets.Destroy();
                    return;
                }

                // Someone else owns YooAssets, so only our own package goes away. The destroy
                // runs asynchronously and the package stays registered until it finishes; a
                // later initialization re-initializes it instead of adopting a dead one.
                //
                // Limitation: this branch is asynchronous and Dispose does not wait for it.
                // Recreating the same package immediately after disposing this initializer is
                // therefore not guaranteed to succeed — the previous destroy may still be in
                // flight. That is accepted rather than designed around: the only current owner
                // of a package is also the owner of YooAssets, which takes the synchronous path
                // above. Do not add lifecycle machinery for this until a second owner exists.
                if (_ownsPackage && package != null && package.InitializeStatus != EOperationStatus.None)
                    package.DestroyPackageAsync();
            }
            finally
            {
                _ownsPackage = false;
                _ownsYooAssets = false;
            }
        }

        private sealed class InitializationResultSource : IUniTaskSource
        {
            private readonly YooAssetContentInitializer _owner;
            private readonly UniTask<ExceptionDispatchInfo> _result;

            public InitializationResultSource(YooAssetContentInitializer owner, UniTask<ExceptionDispatchInfo> result)
            {
                _owner = owner;
                _result = result;
            }

            // Keep the original cancellation exception so a stopped gate remains distinguishable from cancellation.
            public void GetResult(short token) => _result.GetAwaiter().GetResult()?.Throw();

            public UniTaskStatus GetStatus(short token)
            {
                var status = _result.Status;
                if (status != UniTaskStatus.Succeeded || _owner._initializationFailure == null)
                    return status;

                return _owner._initializationFailure.SourceException is OperationCanceledException
                    ? UniTaskStatus.Canceled
                    : UniTaskStatus.Faulted;
            }

            public UniTaskStatus UnsafeGetStatus() => GetStatus(0);

            public void OnCompleted(Action<object> continuation, object state, short token)
                => _result.GetAwaiter().SourceOnCompleted(continuation, state);
        }
    }
}
