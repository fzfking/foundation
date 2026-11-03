using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Content
{
    /// <summary>
    /// Owns the handles of everything one screen, popup or presenter loaded through
    /// <see cref="IAssetProvider"/>. Disposing it releases them all, so the owner keeps no list and cannot lose a
    /// handle on an error path.
    /// </summary>
    public sealed class AssetHandleScope : IDisposable
    {
        private readonly IAssetProvider _assetProvider;
        private readonly List<IDisposable> _handles = new();
        private bool _disposed;

        public int Count => _handles.Count;

        public AssetHandleScope(IAssetProvider assetProvider)
        {
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            var handles = new List<IDisposable>(_handles);
            _handles.Clear();

            List<Exception> failures = null;
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    handles[i].Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            if (failures != null)
                throw new AggregateException("One or more asset handles could not be released.", failures);
        }

        /// <summary>Loads an asset and keeps it alive until the scope is disposed.</summary>
        /// <exception cref="InvalidOperationException">The address resolved to no asset.</exception>
        public async UniTask<T> LoadAsync<T>(string address, CancellationToken cancellationToken = default)
            where T : Object
        {
            ThrowIfDisposed();
            var handle = await _assetProvider.LoadAssetAsync<T>(address, cancellationToken);
            if (handle?.Asset == null)
            {
                handle?.Dispose();
                throw new InvalidOperationException($"Asset '{address}' could not be loaded.");
            }

            if (_disposed)
            {
                // The owner went away while the load was in flight.
                handle.Dispose();
                throw new ObjectDisposedException(nameof(AssetHandleScope));
            }

            _handles.Add(handle);
            return handle.Asset;
        }

        /// <summary>
        /// Loads a prefab and returns its root component <typeparamref name="T"/>, for item prefabs
        /// instantiated many times from one loaded asset. Throws <see cref="InvalidOperationException"/> when
        /// the prefab is missing or has no such component.
        /// </summary>
        public async UniTask<T> LoadComponentAsync<T>(string address, CancellationToken cancellationToken = default)
            where T : Component
        {
            var prefab = await LoadAsync<GameObject>(address, cancellationToken);
            var component = prefab.GetComponent<T>();
            if (component == null)
                throw new InvalidOperationException($"Prefab '{address}' has no {typeof(T).Name}.");
            return component;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AssetHandleScope));
        }
    }
}
