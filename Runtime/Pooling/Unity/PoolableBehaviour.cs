using System.Threading;
using UnityEngine;

namespace Foundation.Pooling
{
    /// <summary>
    /// Convenience base for a component on a pooled prefab; override only the hooks you need.
    /// <see cref="PoolLifetime"/> is cancelled when the instance returns to the pool, so async work started while
    /// rented stops with it. Created on first use after a rent, so unused tokens allocate nothing.
    /// </summary>
    public abstract class PoolableBehaviour : MonoBehaviour, IPoolable
    {
        private CancellationTokenSource _lifetime;

        protected CancellationToken PoolLifetime => (_lifetime ??= new CancellationTokenSource()).Token;

        void IPoolable.OnCreated() => OnPoolCreated();

        void IPoolable.OnRented() => OnPoolRented();

        void IPoolable.OnReturned()
        {
            EndLifetime();
            OnPoolReturned();
        }

        void IPoolable.OnDestroying()
        {
            EndLifetime();
            OnPoolDestroying();
        }

        protected virtual void OnPoolCreated()
        {
        }

        protected virtual void OnPoolRented()
        {
        }

        protected virtual void OnPoolReturned()
        {
        }

        protected virtual void OnPoolDestroying()
        {
        }

        private void EndLifetime()
        {
            if (_lifetime == null)
                return;

            _lifetime.Cancel();
            _lifetime.Dispose();
            _lifetime = null;
        }
    }
}
