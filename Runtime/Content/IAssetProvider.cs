using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Content
{
    /// <summary>
    /// Loads, instantiates and releases assets by address.
    /// <para>
    /// Call every method, and dispose every handle returned, on the Unity main thread. The interface does not promise
    /// thread safety: implementations are not required to support concurrent calls.
    /// </para>
    /// <para>
    /// Ownership: a handle from <c>LoadAssetAsync</c> holds one reference to its asset. Dispose it when the owner stops
    /// using the asset; the provider releases the underlying load once the last reference goes. Disposing the provider
    /// releases the loads it has registered, even while handles for them are still alive.
    /// </para>
    /// </summary>
    public interface IAssetProvider : IDisposable
    {
        /// <summary>
        /// Loads an asset and returns a handle that owns one reference to it. Dispose the handle to release that
        /// reference. A cancelled request returns no handle and releases only its own claim.
        /// </summary>
        UniTask<IAssetHandle<T>> LoadAssetAsync<T>(
            string assetId,
            CancellationToken cancellationToken = default)
            where T : Object;

        /// <summary>
        /// Instantiates a prefab and returns the instance. Disposing it destroys the GameObject and releases the
        /// prefab reference; destroying the GameObject first releases that reference too.
        /// </summary>
        UniTask<IAssetInstance> InstantiateAsync(
            string assetId,
            Transform parent = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// True when <paramref name="assetId"/> resolves to an asset of type <typeparamref name="T"/> in the
        /// catalog. Synchronous, loads nothing.
        /// </summary>
        bool ContainsAsset<T>(string assetId)
            where T : Object;

        /// <summary>
        /// Loads a set of assets and returns one handle that keeps them loaded until it is disposed. If a load fails,
        /// handles already acquired are released and the failure is rethrown.
        /// </summary>
        UniTask<IPreloadHandle> PreloadAsync(
            IReadOnlyList<string> assetIds,
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default);
    }
}
