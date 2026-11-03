using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Lifecycle;

namespace Foundation.Infrastructure.YooAsset
{
    /// <summary>
    /// The content phase of the boot on a host-mode package. With <see cref="Foundation.Content.ContentStartupStrategy.OfflineFirst"/>
    /// it starts from the version on the device, then moves to a newer remote one when it can: a device that has a version
    /// boots without a connection; one that has none waits for it and shows <paramref name="waitingForConnection"/>
    /// meanwhile. Unlike the Addressables step, the gate (<c>beforeRemoteUpdate</c>) runs before a manifest is loaded
    /// or the host is contacted. With <see cref="Foundation.Content.ContentStartupStrategy.Standard"/> the
    /// <paramref name="synchronizer"/> is not needed and the initializer does YooAsset's plain host start; the gate still runs
    /// before the host is asked for the newest version.
    /// The initializer outlives the step: it owns the package for the whole session.
    /// </summary>
    public sealed class YooAssetContentStartupBootStep : IBootStep
    {
        private readonly YooAssetContentInitializer _initializer;
        private readonly YooAssetContentSynchronizer _synchronizer;
        private readonly ILoadingCurtain _curtain;
        private readonly Func<BootMessage> _waitingForConnection;
        private readonly Func<CancellationToken, UniTask<BootStepOutcome>> _beforeRemoteUpdate;

        public string Name => "Content";

        public int Weight { get; }

        /// <param name="synchronizer">The one passed to the initializer; null when the settings choose the standard strategy.</param>
        /// <param name="curtain">Shows the waiting-for-connection state; null shows none.</param>
        /// <param name="waitingForConnection">The text of that state, resolved when it is shown.</param>
        /// <param name="beforeRemoteUpdate">Runs before anything is requested from the host; <see cref="BootStepOutcome.Stop"/> ends the boot.</param>
        public YooAssetContentStartupBootStep(
            YooAssetContentInitializer initializer,
            YooAssetContentSynchronizer synchronizer,
            ILoadingCurtain curtain,
            Func<BootMessage> waitingForConnection,
            Func<CancellationToken, UniTask<BootStepOutcome>> beforeRemoteUpdate = null,
            int weight = 1)
        {
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "A boot step weight cannot be negative.");

            _initializer = initializer ?? throw new ArgumentNullException(nameof(initializer));
            _synchronizer = synchronizer;
            _curtain = curtain;
            _waitingForConnection = waitingForConnection;
            _beforeRemoteUpdate = beforeRemoteUpdate;
            Weight = weight;
        }

        public async UniTask<BootStepOutcome> RunAsync(IProgress<float> progress, CancellationToken cancellationToken)
        {
            void OnStateChanged(YooAssetContentSynchronizer.SynchronizationState state)
                => ShowConnectionState(state == YooAssetContentSynchronizer.SynchronizationState.WaitingForConnection);

            if (_synchronizer != null)
                _synchronizer.StateChanged += OnStateChanged;
            try
            {
                await _initializer.InitializeAsync(cancellationToken, progress, AllowRemoteUpdateAsync);
                return BootStepOutcome.Continue;
            }
            catch (ContentUpdateStoppedException)
            {
                return BootStepOutcome.Stop;
            }
            finally
            {
                if (_synchronizer != null)
                    _synchronizer.StateChanged -= OnStateChanged;
                ShowConnectionState(false);
            }
        }

        private async UniTask<bool> AllowRemoteUpdateAsync(CancellationToken cancellationToken)
            => _beforeRemoteUpdate == null || await _beforeRemoteUpdate(cancellationToken) == BootStepOutcome.Continue;

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
    }
}
