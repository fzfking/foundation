using System;
using System.Threading;
using Foundation.Content;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Pooling
{
    /// <summary>
    /// Where a pool gets its prefab. Separates who loads the asset and who owns its lifetime from pooling: a
    /// plain reference, an Addressables handle (through <see cref="IAssetProvider"/>) and others look the
    /// same to the pool.
    /// </summary>
    public interface IPrefabSource
    {
        UniTask<IPrefabLease> AcquireAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// A hold on a loaded prefab. The pool that acquired it owns it and disposes it after destroying its
    /// instances, which is when a provider may unload the asset.
    /// </summary>
    public interface IPrefabLease : IDisposable
    {
        GameObject Prefab { get; }
    }

    public sealed class DirectPrefabSource : IPrefabSource, IPrefabLease
    {
        public GameObject Prefab { get; }

        public DirectPrefabSource(GameObject prefab)
        {
            Prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
        }

        public void Dispose()
        {
        }

        public UniTask<IPrefabLease> AcquireAsync(CancellationToken cancellationToken) => UniTask.FromResult<IPrefabLease>(this);
    }

    public sealed class AssetProviderPrefabSource : IPrefabSource
    {
        private readonly IAssetProvider _assets;
        private readonly string _assetId;

        public AssetProviderPrefabSource(IAssetProvider assets, string assetId)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _assetId = string.IsNullOrWhiteSpace(assetId) ? throw new ArgumentException("Asset id is required.", nameof(assetId)) : assetId;
        }

        public async UniTask<IPrefabLease> AcquireAsync(CancellationToken cancellationToken)
        {
            var handle = await _assets.LoadAssetAsync<GameObject>(_assetId, cancellationToken);
            return new HandleLease(handle);
        }

        private sealed class HandleLease : IPrefabLease
        {
            private readonly IAssetHandle<GameObject> _handle;

            public GameObject Prefab => _handle.Asset;

            public HandleLease(IAssetHandle<GameObject> handle)
            {
                _handle = handle;
            }

            public void Dispose() => _handle.Dispose();
        }
    }
}
