using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Foundation.Infrastructure.Addressables
{
    /// <summary>
    /// Rejects at once when the device reports no network (<see cref="Application.internetReachability"/>),
    /// otherwise sends a small HEAD request to <paramref name="probeUrl"/> with a short timeout. Use a
    /// dedicated probe URL, not the catalog: this answers only "is the host up".
    /// </summary>
    public sealed class UnityWebRequestHostReachabilityChecker : IContentHostReachabilityChecker, INetworkReachabilityStatusProvider
    {
        private readonly string _probeUrl;
        private readonly int _timeoutSeconds;

        public UnityWebRequestHostReachabilityChecker(string probeUrl, int timeoutSeconds = 5)
        {
            if (string.IsNullOrWhiteSpace(probeUrl))
                throw new ArgumentException("Probe URL must not be empty.", nameof(probeUrl));
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));

            _probeUrl = probeUrl;
            _timeoutSeconds = timeoutSeconds;
        }

        public bool IsNetworkUnavailable => Application.internetReachability == NetworkReachability.NotReachable;

        public async UniTask<bool> IsReachableAsync(CancellationToken cancellationToken = default)
        {
            if (IsNetworkUnavailable)
                return false;

            cancellationToken.ThrowIfCancellationRequested();

            using var request = UnityWebRequest.Head(_probeUrl);
            request.timeout = _timeoutSeconds;

            var operation = request.SendWebRequest();
            try
            {
                await UniTask.WaitUntil(() => operation.isDone, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                request.Abort();
                throw;
            }

            return request.result != UnityWebRequest.Result.ConnectionError
                && request.result != UnityWebRequest.Result.DataProcessingError;
        }
    }
}
