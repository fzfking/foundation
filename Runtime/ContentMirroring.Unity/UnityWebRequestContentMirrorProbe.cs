using System;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.ContentMirroring;
using UnityEngine.Networking;

namespace Foundation.Infrastructure.ContentMirroring
{
    public sealed class UnityWebRequestContentMirrorProbe : IContentMirrorProbe
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestContentMirrorProbe(int timeoutSeconds = 8)
        {
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<ContentMirrorProbeResult> ProbeAsync(
            ContentMirror mirror,
            string relativePath,
            int expectedSizeBytes,
            CancellationToken cancellationToken)
        {
            using var request = UnityWebRequest.Get(mirror.GetUrl(relativePath));
            request.timeout = _timeoutSeconds;
            var stopwatch = Stopwatch.StartNew();
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    request.Abort();
                    while (!operation.isDone)
                        await UniTask.Yield();
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await UniTask.Yield();
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success
                || request.responseCode < 200
                || request.responseCode >= 300
                || request.downloadedBytes != (ulong)expectedSizeBytes)
                return new ContentMirrorProbeResult(false, 0d);

            var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001d);
            return new ContentMirrorProbeResult(true, expectedSizeBytes / seconds);
        }
    }
}
