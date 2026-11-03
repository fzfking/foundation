using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Content;
using Foundation.ContentMirroring;
using Foundation.Lifecycle;
using UnityEngine.ResourceManagement.AsyncOperations;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// The content phase of the boot: initializes Addressables against the local or cached catalog, then downloads
    /// whatever content the device lacks. Offline-first - a device that has all of its content boots without a
    /// connection; one that lacks some waits for it and shows <paramref name="waitingForConnection"/> meanwhile.
    /// Between the two parts a project can run a gate (<c>afterInitialize</c>), typically the release check: it runs
    /// before anything is downloaded, so a client that must update never downloads content it cannot play.
    /// With <see cref="ContentStartupStrategy.Standard"/> the step is only Addressables' own initialization followed by the
    /// gate: no startup download, no waiting for a connection, and a failure ends the boot with an exception.
    /// </summary>
    public sealed class ContentStartupBootStep : IBootStep
    {
        private const float DefaultInitializationShare = 0.2f;

        private readonly IContentHostReachabilityChecker _reachability;
        private readonly ILoadingCurtain _curtain;
        private readonly Func<BootMessage> _waitingForConnection;
        private readonly Func<CancellationToken, UniTask<BootStepOutcome>> _afterInitialize;
        private readonly float _initializationShare;
        private readonly ContentStartupStrategy _strategy;
        private readonly ContentMirrorCoordinator _mirrorCoordinator;

        public string Name => "Content";

        public int Weight { get; }

        /// <param name="reachability">Probes the content host before the download.</param>
        /// <param name="curtain">Shows the waiting-for-connection state; null shows none.</param>
        /// <param name="waitingForConnection">The text of that state, resolved when it is shown.</param>
        /// <param name="afterInitialize">Runs once the catalog is initialized; <see cref="BootStepOutcome.Stop"/> ends the boot.</param>
        /// <param name="initializationShare">The part of the step's bar that catalog initialization fills; the download fills the rest.</param>
        /// <param name="strategy">The project's choice, normally read from its own settings.</param>
        public ContentStartupBootStep(
            IContentHostReachabilityChecker reachability,
            ILoadingCurtain curtain,
            Func<BootMessage> waitingForConnection,
            Func<CancellationToken, UniTask<BootStepOutcome>> afterInitialize = null,
            int weight = 1,
            float initializationShare = DefaultInitializationShare,
            ContentStartupStrategy strategy = ContentStartupStrategy.OfflineFirst,
            ContentMirrorCoordinator mirrorCoordinator = null)
        {
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "A boot step weight cannot be negative.");
            if (initializationShare < 0f || initializationShare > 1f)
                throw new ArgumentOutOfRangeException(nameof(initializationShare), initializationShare, "The share is a fraction from 0 to 1.");

            _reachability = reachability ?? throw new ArgumentNullException(nameof(reachability));
            _curtain = curtain;
            _waitingForConnection = waitingForConnection;
            _afterInitialize = afterInitialize;
            Weight = weight;
            _initializationShare = initializationShare;
            _strategy = strategy;
            _mirrorCoordinator = mirrorCoordinator;
        }

        public UniTask<BootStepOutcome> RunAsync(IProgress<float> progress, CancellationToken cancellationToken)
            => _strategy == ContentStartupStrategy.Standard
                ? RunStandardAsync(progress, cancellationToken)
                : RunOfflineFirstAsync(progress, cancellationToken);

        private async UniTask<BootStepOutcome> RunStandardAsync(IProgress<float> progress, CancellationToken cancellationToken)
        {
            var handle = AddressablesApi.InitializeAsync(false);
            try
            {
                while (!handle.IsDone)
                {
                    progress?.Report(handle.PercentComplete);
                    await UniTask.Yield(cancellationToken);
                }

                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException("Addressables initialization failed.", handle.OperationException);
            }
            finally
            {
                handle.Release();
            }

            progress?.Report(1f);
            if (_afterInitialize != null && await _afterInitialize(cancellationToken) == BootStepOutcome.Stop)
                return BootStepOutcome.Stop;

            return BootStepOutcome.Continue;
        }

        private async UniTask<BootStepOutcome> RunOfflineFirstAsync(IProgress<float> progress, CancellationToken cancellationToken)
        {
            // Initialization uses the local or cached catalog without a network request (Addressables' automatic
            // catalog updates are off in the settings); the synchronizer checks for updates once the host is reachable.
            var initializer = new AddressablesContentInitializer();
            var synchronizer = new AddressablesContentSynchronizer(
                _reachability,
                mirrorCoordinator: _mirrorCoordinator);

            void OnInitializerStateChanged(AddressablesContentInitializer.InitializationState state)
                => ShowConnectionState(state == AddressablesContentInitializer.InitializationState.WaitingForConnection);

            void OnSynchronizerStateChanged(AddressablesContentSynchronizer.SynchronizationState state)
                => ShowConnectionState(state == AddressablesContentSynchronizer.SynchronizationState.WaitingForConnection);

            initializer.StateChanged += OnInitializerStateChanged;
            synchronizer.StateChanged += OnSynchronizerStateChanged;
            try
            {
                await initializer.InitializeAsync(
                    cancellationToken,
                    progress == null ? null : new ScaledProgress(progress, 0f, _initializationShare));

                if (_afterInitialize != null && await _afterInitialize(cancellationToken) == BootStepOutcome.Stop)
                    return BootStepOutcome.Stop;

                await synchronizer.SynchronizeForStartupAsync(
                    progress == null ? null : new ScaledProgress(progress, _initializationShare, 1f - _initializationShare),
                    cancellationToken);
                return BootStepOutcome.Continue;
            }
            finally
            {
                initializer.StateChanged -= OnInitializerStateChanged;
                synchronizer.StateChanged -= OnSynchronizerStateChanged;
                initializer.Dispose();
            }
        }

        private void ShowConnectionState(bool waiting)
        {
            if (_curtain == null)
                return;

            if (!waiting || _waitingForConnection == null)
            {
                _curtain.ClearWaitingForConnection();
                return;
            }

            var message = _waitingForConnection();
            _curtain.ShowWaitingForConnection(message.Title, message.Description);
        }

        private sealed class ScaledProgress : IProgress<float>
        {
            private readonly IProgress<float> _inner;
            private readonly float _offset;
            private readonly float _share;

            public ScaledProgress(IProgress<float> inner, float offset, float share)
            {
                _inner = inner;
                _offset = offset;
                _share = share;
            }

            public void Report(float value) => _inner.Report(_offset + value * _share);
        }
    }
}
