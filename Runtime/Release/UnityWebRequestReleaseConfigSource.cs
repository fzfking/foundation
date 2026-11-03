using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace Foundation.Release
{
    /// <summary>Fetches the release configuration text over HTTP(S) with a request timeout.</summary>
    public interface IReleaseConfigSource
    {
        UniTask<string> FetchAsync(string url, CancellationToken cancellationToken = default);
    }

    public sealed class UnityWebRequestReleaseConfigSource : IReleaseConfigSource
    {
        private readonly int _timeoutSeconds;

        public UnityWebRequestReleaseConfigSource(int timeoutSeconds)
        {
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<string> FetchAsync(string url, CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = _timeoutSeconds;
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);

            return request.result != UnityWebRequest.Result.Success 
                ? throw new InvalidOperationException($"Release configuration request failed: {request.error}") 
                : request.downloadHandler.text;
        }
    }
}
