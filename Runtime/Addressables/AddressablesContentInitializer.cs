using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.ResourceManagement.AsyncOperations;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Offline-first wrapper around <c>Addressables.InitializeAsync</c>. A device with cached content must
    /// not block boot on a flaky connection; a device with nothing cached has nothing to boot into, so it
    /// waits for a connection before trying initialization.
    /// Before initialization:
    /// - reachable, or no <see cref="IContentHostReachabilityChecker"/> configured: call
    /// <c>InitializeAsync</c> directly;
    /// - unreachable but <see cref="HasLocalCache"/>: still call it, Addressables falls back to the last
    /// loaded catalog;
    /// - unreachable and no cache: wait for the host to become reachable.
    /// An Addressables initialization failure is reported to the caller; Addressables can retain the failed
    /// operation, so repeatedly calling it does not necessarily start a new attempt.
    /// </summary>
    public sealed class AddressablesContentInitializer : IDisposable
    {
        private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(3);

        private readonly IContentHostReachabilityChecker _reachabilityChecker;
        private readonly TimeSpan _retryInterval;
        private readonly CancellationTokenSource _disposeCts = new();

        private AsyncLazy<ExceptionDispatchInfo> _initialization;
        private bool _disposed;

        public InitializationState State { get; private set; } = InitializationState.NotStarted;

        public event Action<InitializationState> StateChanged;

        public static bool HasLocalCache => Caching.currentCacheForWriting.spaceOccupied > 0;

        public AddressablesContentInitializer(
            IContentHostReachabilityChecker reachabilityChecker = null,
            TimeSpan? retryInterval = null)
        {
            _reachabilityChecker = reachabilityChecker;
            _retryInterval = retryInterval ?? DefaultRetryInterval;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }

        /// <summary>
        /// Shares one initialization among callers. The first caller controls the operation's cancellation and
        /// progress; later callers can cancel their own wait without canceling the shared operation.
        /// </summary>
        public async UniTask InitializeAsync(
            CancellationToken cancellationToken = default,
            IProgress<float> progress = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AddressablesContentInitializer));
            var isFirstCall = _initialization == null;
            _initialization ??= new AsyncLazy<ExceptionDispatchInfo>(
                () => InitializeSharedAsync(cancellationToken, progress));
            var result = _initialization.Task;
            if (!isFirstCall)
                result = result.AttachExternalCancellation(cancellationToken);

            var failure = await result;
            failure?.Throw();
        }

        private async UniTask<ExceptionDispatchInfo> InitializeSharedAsync(CancellationToken cancellationToken, IProgress<float> progress)
        {
            try
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);
                await InitializeInternalAsync(linkedCts.Token, progress);
                return null;
            }
            catch (Exception exception)
            {
                return ExceptionDispatchInfo.Capture(exception);
            }
        }

        private async UniTask InitializeInternalAsync(CancellationToken cancellationToken, IProgress<float> progress)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var reachable = _reachabilityChecker == null
                    || await _reachabilityChecker.IsReachableAsync(cancellationToken);

                if (!reachable && !HasLocalCache)
                {
                    await WaitForReachabilityAsync(cancellationToken);
                    continue;
                }

                SetState(InitializationState.Initializing);

                var handle = AddressablesApi.InitializeAsync(false);
                try
                {
                    while (!handle.IsDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(handle.PercentComplete);
                        await UniTask.Yield(cancellationToken);
                    }

                    if (handle.Status != AsyncOperationStatus.Succeeded)
                        throw new InvalidOperationException("Addressables initialization failed.", handle.OperationException);

                    SetState(InitializationState.Succeeded);
                    return;
                }
                finally
                {
                    handle.Release();
                }
            }
        }

        private async UniTask WaitForReachabilityAsync(CancellationToken cancellationToken)
        {
            SetState(InitializationState.WaitingForConnection);

            while (!await _reachabilityChecker.IsReachableAsync(cancellationToken))
            {
                await UniTask.Delay(_retryInterval, cancellationToken: cancellationToken);
            }
        }

        private void SetState(InitializationState state)
        {
            if (State == state)
                return;

            State = state;
            StateChanged?.Invoke(state);
        }

        public enum InitializationState
        {
            NotStarted,
            Initializing,
            WaitingForConnection,
            Succeeded,
        }
    }
}
