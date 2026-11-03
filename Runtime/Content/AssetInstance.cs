using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Content
{
    public interface IAssetInstance : IDisposable
    {
        GameObject GameObject { get; }
    }

    internal sealed class AssetInstance : IAssetInstance
    {
        private Action _releaseAction;
        private GameObject _gameObject;
        private AssetInstanceTracker _tracker;
        private bool _disposed;
        private bool _externallyDestroyed;

        public GameObject GameObject => _gameObject;

        public AssetInstance(GameObject gameObject, Action releaseAction)
        {
            _gameObject = gameObject;
            _releaseAction = releaseAction;

            _tracker = _gameObject.AddComponent<AssetInstanceTracker>();
            _tracker.Initialize(OnExternalDestroy);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            if (!_externallyDestroyed && _gameObject != null)
            {
                if (_tracker != null)
                {
                    _tracker.ClearCallback();
                    _tracker = null;
                }

                Object.Destroy(_gameObject);
                _gameObject = null;
            }

            _releaseAction?.Invoke();
            _releaseAction = null;
        }

        private void OnExternalDestroy()
        {
            _externallyDestroyed = true;
            _gameObject = null;

            if (_disposed)
                return;

            _disposed = true;

            _releaseAction?.Invoke();
            _releaseAction = null;
        }
    }
}
