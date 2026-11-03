using System;
using Object = UnityEngine.Object;

namespace Foundation.Content
{
    public interface IAssetHandle<out T> : IDisposable
        where T : Object
    {
        T Asset { get; }
    }

    internal sealed class AssetHandle<T> : IAssetHandle<T>
        where T : Object
    {
        private Action _releaseAction;
        private bool _disposed;

        public T Asset { get; }

        public AssetHandle(T asset, Action releaseAction)
        {
            Asset = asset;
            _releaseAction = releaseAction;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _releaseAction?.Invoke();
            _releaseAction = null;
        }
    }
}
