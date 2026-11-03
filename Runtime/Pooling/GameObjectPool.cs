using System;
using Foundation.DI;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Pooling
{
    public interface IGameObjectPool : IDisposable
    {
        GameObject Rent(Transform parent = null);
        void Return(GameObject instance);
        void Clear();
    }

    public sealed class GameObjectPool : IGameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _poolRoot;
        private readonly IGameObjectInjector _injector;
        private readonly ObjectPool<GameObject> _pool;
        private bool _disposed;

        public GameObjectPool(
            GameObject prefab,
            Transform poolRoot = null,
            int maxSize = int.MaxValue,
            IGameObjectInjector injector = null)
        {
            _prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            _poolRoot = poolRoot;
            _injector = injector;
            _pool = new(Create, OnRent, OnReturn, OnDestroyInstance, maxSize);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _pool.Clear();
        }

        public GameObject Rent(Transform parent = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameObjectPool));

            var instance = _pool.Rent();
            instance.transform.SetParent(parent, false);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (_disposed)
            {
                if (instance != null)
                    Object.Destroy(instance);
                return;
            }

            _pool.Return(instance);
        }

        public void Clear() => _pool.Clear();

        // Injection happens once, here, at creation — a returned-and-re-rented instance keeps
        // whatever it was injected with the first time, it is not re-injected on every Rent.
        private GameObject Create()
        {
            var instance = Object.Instantiate(_prefab);
            _injector?.Inject(instance);
            return instance;
        }

        private void OnRent(GameObject instance) => instance.SetActive(true);

        private void OnReturn(GameObject instance)
        {
            instance.SetActive(false);
            instance.transform.SetParent(_poolRoot, false);
        }

        private static void OnDestroyInstance(GameObject instance)
        {
            if (instance != null)
                Object.Destroy(instance);
        }
    }
}
