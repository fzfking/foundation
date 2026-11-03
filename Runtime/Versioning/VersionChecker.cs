using System.Threading;
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;
using UnityEngine.Networking;

namespace Foundation.Versioning
{
    public interface IVersionChecker
    {
        UniTask<VersionCheckResult> CheckAsync(CancellationToken cancellationToken = default);
    }

    public sealed class UnityVersionChecker : IVersionChecker
    {
        private readonly string _versionUrl;
        private readonly int _timeoutSeconds;

        public UnityVersionChecker(string versionUrl, int timeoutSeconds = 15)
        {
            _versionUrl = versionUrl;
            _timeoutSeconds = timeoutSeconds;
        }

        public async UniTask<VersionCheckResult> CheckAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(_versionUrl))
                return new VersionCheckResult(Application.version, Application.version, true);
            using var request = UnityWebRequest.Get(_versionUrl);
            // Without a timeout a host that accepts the connection and says nothing keeps the check pending for good.
            request.timeout = _timeoutSeconds;
            await request.SendWebRequest().ToUniTask(cancellationToken: cancellationToken);
            if (request.result != UnityWebRequest.Result.Success)
                throw new InvalidOperationException($"Version check failed for '{_versionUrl}': {request.error}");
            var current = Application.version.Trim();
            var latest = request.downloadHandler.text.Trim();
            if (!Version.TryParse(current, out var currentVersion) || !Version.TryParse(latest, out var latestVersion))
                throw new InvalidOperationException($"Version file must contain a semantic version. Current='{current}', latest='{latest}'.");
            return new VersionCheckResult(current, latest, currentVersion >= latestVersion);
        }
    }
}
