using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.ResourceManagement.AsyncOperations;
using AddressablesApi = UnityEngine.AddressableAssets.Addressables;

namespace Foundation.Infrastructure.Addressables
{
    public static class AddressablesAssetCache
    {
        /// <summary>Clears the bundles backing this address; returns false when Addressables cannot evict the cache.</summary>
        public static async UniTask<bool> ClearDependenciesAsync(
            string assetAddress,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(assetAddress))
                throw new ArgumentException("An asset address is required.", nameof(assetAddress));

            cancellationToken.ThrowIfCancellationRequested();
            var handle = AddressablesApi.ClearDependencyCacheAsync((object)assetAddress, false);
            try
            {
                await WaitAsync(handle, cancellationToken);
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to clear Addressables dependencies for '{assetAddress}': {handle.OperationException?.Message}");

                return handle.Result;
            }
            finally
            {
                handle.Release();
            }
        }

        public static async UniTask<long> GetDownloadSizeAsync(
            string assetAddress,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(assetAddress))
                throw new ArgumentException("An asset address is required.", nameof(assetAddress));

            cancellationToken.ThrowIfCancellationRequested();
            var handle = AddressablesApi.GetDownloadSizeAsync((object)assetAddress);
            try
            {
                await WaitAsync(handle, cancellationToken);
                if (handle.Status != AsyncOperationStatus.Succeeded)
                    throw new InvalidOperationException(
                        $"Failed to get Addressables download size for '{assetAddress}': {handle.OperationException?.Message}");

                return handle.Result;
            }
            finally
            {
                handle.Release();
            }
        }

        private static async UniTask WaitAsync<T>(
            AsyncOperationHandle<T> handle,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!handle.IsDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                while (!handle.IsDone)
                    await UniTask.Yield();
                throw;
            }
        }
    }
}
