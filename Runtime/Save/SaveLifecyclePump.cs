using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Logging;
using UnityEngine;

namespace Foundation.Save
{
    /// <summary>
    /// Application-owned save pump shared by every game context: periodic checkpoints, pause/quit
    /// checkpoints and the resume hook. The active context attaches its own orchestrator and
    /// callbacks on start and detaches them on stop, so this type knows nothing about any
    /// context's gameplay (offline income, simulation clocks, ...). One instance lives in the
    /// application's root scene and is handed to the contexts.
    /// </summary>
    public sealed class SaveLifecyclePump : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float periodicSaveSeconds = 30f;

        private SaveOrchestrator _save;
        private Action _markActive;
        private Action _onResumed;
        private IAppLogger _logger;
        private CancellationTokenSource _periodicSaveCancellation;
        private CancellationTokenSource _activeSaveCancellation;
        private bool _saving;
        private bool _saveRequested;
        private bool _initialized;
        private bool _acceptRequests = true;

        private void OnDestroy()
        {
            _activeSaveCancellation?.Cancel();
            _activeSaveCancellation?.Dispose();
            _activeSaveCancellation = null;
            _periodicSaveCancellation?.Cancel();
            _periodicSaveCancellation?.Dispose();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                // Android can freeze this process's threads almost immediately after this handler
                // returns - it does not reliably let a pending async continuation (like the awaited
                // save below) run to completion first. The checkpoint must happen synchronously,
                // right here, or it may simply never happen. See MarkActiveNow's own doc for why the
                // checkpoint itself matters; PersistAsync's disk write is comparatively best-effort.
                _markActive?.Invoke();
                RequestPersist();
            }
            else
            {
                HandleResume();
            }
        }

        private void OnApplicationQuit()
        {
            _markActive?.Invoke();
            RequestPersist();
        }

        /// <param name="markActive">Synchronous checkpoint taken right before a pause/quit save.</param>
        /// <param name="onResumed">Runs when the application returns from background, before the
        /// resulting state is persisted (e.g. to apply the time spent away).</param>
        public void Initialize(
            SaveOrchestrator save,
            Action markActive = null,
            Action onResumed = null,
            IAppLogger logger = null)
        {
            _save = save;
            _markActive = markActive;
            _onResumed = onResumed;
            _logger = logger;

            _initialized = true;
            ResumePeriodicPersistence();
        }

        public async UniTask PrepareForDataResetAsync(CancellationToken cancellationToken = default)
        {
            _acceptRequests = false;
            _saveRequested = false;
            _periodicSaveCancellation?.Cancel();
            _periodicSaveCancellation?.Dispose();
            _periodicSaveCancellation = null;
            _activeSaveCancellation?.Cancel();

            await UniTask.WaitUntil(() => !_saving, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Stops persistence for a context that is shutting down and forgets its orchestrator and
        /// callbacks, so pause/resume notifications can never reach a disposed context scope.
        /// </summary>
        public async UniTask DetachAsync(SaveOrchestrator save, CancellationToken cancellationToken = default)
        {
            if (!ReferenceEquals(_save, save))
                return;

            await PrepareForDataResetAsync(cancellationToken);
            if (!ReferenceEquals(_save, save))
                return;

            _save = null;
            _markActive = null;
            _onResumed = null;
        }

        public void ResumePeriodicPersistence()
        {
            _acceptRequests = true;
            if (!_initialized || _periodicSaveCancellation != null)
                return;

            _periodicSaveCancellation = new CancellationTokenSource();
            RunPeriodicSaveAsync(_periodicSaveCancellation.Token).Forget();
        }

        // Does not wait for PersistAsync: a prior pause's save can still be in flight, or never have resumed if Android
        // suspended the process mid-await, and the next PersistAsync picks up a pending save anyway.
        private void HandleResume()
        {
            if (_onResumed == null)
                return;

            _onResumed();
            RequestPersist();
        }

        private void RequestPersist()
        {
            if (!_acceptRequests)
                return;

            _saveRequested = true;
            if (!_saving)
                PersistAsync().Forget();
        }

        private async UniTask PersistAsync()
        {
            if (_saving || _save == null)
                return;

            _saving = true;
            using var saveCancellation = new CancellationTokenSource();
            _activeSaveCancellation = saveCancellation;
            try
            {
                while (_saveRequested)
                {
                    _saveRequested = false;
                    await _save.SaveAsync(saveCancellation.Token);
                }
            }
            catch (OperationCanceledException) when (saveCancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                if (_logger != null)
                    _logger.Log(LogLevel.Warning, "Save", $"checkpoint failed: {exception.Message}");
                else
                    Debug.LogWarning($"Save checkpoint failed: {exception.Message}");
            }
            finally
            {
                if (ReferenceEquals(_activeSaveCancellation, saveCancellation))
                    _activeSaveCancellation = null;
                _saving = false;
                if (_saveRequested)
                    PersistAsync().Forget();
            }
        }

        private async UniTask RunPeriodicSaveAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(periodicSaveSeconds),
                        ignoreTimeScale: true,
                        cancellationToken: cancellationToken);
                    RequestPersist();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}
