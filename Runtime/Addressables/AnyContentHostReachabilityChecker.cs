using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>Reports reachable when any configured content host responds to its lightweight probe.</summary>
    public sealed class AnyContentHostReachabilityChecker : IContentHostReachabilityChecker, INetworkReachabilityStatusProvider
    {
        private readonly IReadOnlyList<IContentHostReachabilityChecker> _checkers;
        private readonly bool _parallelChecks;

        public AnyContentHostReachabilityChecker(IReadOnlyList<string> probeUrls, int timeoutSeconds = 5)
            : this(probeUrls, timeoutSeconds, false)
        {
        }

        public AnyContentHostReachabilityChecker(IReadOnlyList<string> probeUrls, int timeoutSeconds, bool parallelChecks)
        {
            if (probeUrls == null || probeUrls.Count == 0)
                throw new ArgumentException("At least one content host probe URL is required.", nameof(probeUrls));

            var checkers = new List<IContentHostReachabilityChecker>(probeUrls.Count);
            foreach (var url in probeUrls)
                checkers.Add(new UnityWebRequestHostReachabilityChecker(url, timeoutSeconds));
            _checkers = checkers;
            _parallelChecks = parallelChecks;
        }

        internal AnyContentHostReachabilityChecker(IReadOnlyList<IContentHostReachabilityChecker> checkers, bool parallelChecks)
        {
            _checkers = checkers;
            _parallelChecks = parallelChecks;
        }

        // Offline only when every host agrees; a checker that cannot tell keeps the answer unknown.
        public bool IsNetworkUnavailable
        {
            get
            {
                foreach (var checker in _checkers)
                {
                    if (checker is not INetworkReachabilityStatusProvider provider || !provider.IsNetworkUnavailable)
                        return false;
                }

                return true;
            }
        }

        public async UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
        {
            if (_parallelChecks)
                return await IsReachableInParallelAsync(cancellationToken);

            foreach (var checker in _checkers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await checker.IsReachableAsync(cancellationToken))
                    return true;
            }

            return false;
        }

        private async UniTask<bool> IsReachableInParallelAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var pending = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var reachable = false;
            Exception failure = null;
            var checks = new UniTask[_checkers.Count];
            for (var index = 0; index < checks.Length; index++)
                checks[index] = CheckAsync(_checkers[index]);

            await UniTask.WhenAll(checks);
            cancellationToken.ThrowIfCancellationRequested();
            if (reachable)
                return true;
            if (failure != null)
                throw failure;
            return false;

            async UniTask CheckAsync(IContentHostReachabilityChecker checker)
            {
                try
                {
                    if (await checker.IsReachableAsync(pending.Token))
                    {
                        reachable = true;
                        pending.Cancel();
                    }
                }
                catch (OperationCanceledException) when (pending.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    failure ??= exception;
                }
            }
        }
    }
}
