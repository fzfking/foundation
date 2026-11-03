using System;
using System.Threading;
using Foundation.Content;
using Foundation.ContentMirroring;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Infrastructure.HybridClr
{
    /// <summary>Returns assembly payload bytes only after the configured signature verifier accepts them.</summary>
    public interface IHotUpdateAssemblySource
    {
        UniTask<byte[]> LoadVerifiedAsync(string assemblyAddress, CancellationToken cancellationToken = default);
    }

    /// <summary>The mirror callback receives null when every alternate attempt fails, so the caller can restore its original route.</summary>
    public sealed class MirroredHotUpdateAssemblySource : IHotUpdateAssemblySource
    {
        private readonly IAssetProvider _assetProvider;
        private readonly IHotUpdateAssemblyVerifier _verifier;
        private readonly ContentMirrorCoordinator _mirrorCoordinator;
        private readonly Action<ContentMirror> _applyMirror;
        private readonly Func<string> _getCurrentMirrorBaseUrl;
        private readonly Func<string, CancellationToken, UniTask<bool>> _clearDependencyCacheAsync;
        private readonly Func<string, CancellationToken, UniTask<long>> _getDownloadSizeAsync;

        public MirroredHotUpdateAssemblySource(
            IAssetProvider assetProvider,
            IHotUpdateAssemblyVerifier verifier,
            ContentMirrorCoordinator mirrorCoordinator,
            Action<ContentMirror> applyMirror,
            Func<string> getCurrentMirrorBaseUrl,
            Func<string, CancellationToken, UniTask<bool>> clearDependencyCacheAsync,
            Func<string, CancellationToken, UniTask<long>> getDownloadSizeAsync)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
            _mirrorCoordinator = mirrorCoordinator ?? throw new ArgumentNullException(nameof(mirrorCoordinator));
            _applyMirror = applyMirror ?? throw new ArgumentNullException(nameof(applyMirror));
            _getCurrentMirrorBaseUrl = getCurrentMirrorBaseUrl ?? throw new ArgumentNullException(nameof(getCurrentMirrorBaseUrl));
            _clearDependencyCacheAsync = clearDependencyCacheAsync ?? throw new ArgumentNullException(nameof(clearDependencyCacheAsync));
            _getDownloadSizeAsync = getDownloadSizeAsync ?? throw new ArgumentNullException(nameof(getDownloadSizeAsync));
        }

        /// <summary>Retries only this address on another mirror after loading or signature verification fails.</summary>
        public async UniTask<byte[]> LoadVerifiedAsync(
            string assemblyAddress,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(assemblyAddress))
                throw new ArgumentException("An assembly address is required.", nameof(assemblyAddress));

            cancellationToken.ThrowIfCancellationRequested();
            var currentMirrorBaseUrl = _getCurrentMirrorBaseUrl();
            Exception lastFailure;
            try
            {
                return await LoadFromCurrentMirrorAsync(assemblyAddress, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                lastFailure = exception;
            }

            if (!await _clearDependencyCacheAsync(assemblyAddress, cancellationToken))
                throw new InvalidOperationException(
                    $"Could not evict the cached bundle for '{assemblyAddress}' before trying another mirror.",
                    lastFailure);

            var expectedDownloadBytes = await _getDownloadSizeAsync(assemblyAddress, cancellationToken);
            if (expectedDownloadBytes <= 0)
                throw new InvalidOperationException(
                    $"Addressables did not report a fresh download for '{assemblyAddress}' after clearing its cache.",
                    lastFailure);

            byte[] verifiedBytes = null;
            var alternateAttemptCount = 0;
            var succeeded = await _mirrorCoordinator.ExecuteAsync(async (mirror, mirrorProgress, attemptToken) =>
            {
                mirrorProgress.Report(new ContentMirrorProgress(0, expectedDownloadBytes));
                attemptToken.ThrowIfCancellationRequested();

                if (alternateAttemptCount > 0)
                {
                    if (!await _clearDependencyCacheAsync(assemblyAddress, attemptToken))
                    {
                        lastFailure = new InvalidOperationException(
                            $"Could not evict the cached bundle for '{assemblyAddress}' before trying mirror '{mirror.Id}'.");
                        return false;
                    }

                    if (await _getDownloadSizeAsync(assemblyAddress, attemptToken) <= 0)
                    {
                        lastFailure = new InvalidOperationException(
                            $"Addressables did not report a fresh download for '{assemblyAddress}' before trying mirror '{mirror.Id}'.");
                        return false;
                    }
                }

                alternateAttemptCount++;
                _applyMirror(mirror);
                try
                {
                    verifiedBytes = await LoadFromCurrentMirrorAsync(assemblyAddress, attemptToken);
                    return true;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                    return false;
                }
            },
                cancellationToken,
                expectedDownloadBytes,
                currentMirrorBaseUrl);

            if (succeeded)
                return verifiedBytes;

            // Null asks the caller's callback to drop the last mirror and restore its original route.
            _applyMirror(null);
            if (lastFailure is HotUpdateSignatureException signatureException)
                throw signatureException;

            throw new InvalidOperationException(
                $"Could not load a signature-verified assembly '{assemblyAddress}' from another mirror.",
                lastFailure);
        }

        private async UniTask<byte[]> LoadFromCurrentMirrorAsync(
            string assemblyAddress,
            CancellationToken cancellationToken)
        {
            using var handle = await _assetProvider.LoadAssetAsync<TextAsset>(assemblyAddress, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return _verifier.Verify(assemblyAddress, handle.Asset.bytes);
        }
    }
}
