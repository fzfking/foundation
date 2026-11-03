using System;

namespace Foundation.Pooling
{
    public sealed class DelegatePoolStrategy<T> : IPoolStrategy<T>
        where T : class
    {
        private readonly Func<T> _create;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _onDestroy;

        public DelegatePoolStrategy(
            Func<T> create,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
            _onRent = onRent;
            _onReturn = onReturn;
            _onDestroy = onDestroy;
        }

        public T Create() => _create();

        public bool IsValid(T item) => true;

        public void OnRent(T item) => _onRent?.Invoke(item);

        public void OnReturn(T item) => _onReturn?.Invoke(item);

        public void OnDestroy(T item) => _onDestroy?.Invoke(item);
    }

    public sealed class PoolableStrategy<T> : IPoolStrategy<T>
        where T : class, IPoolable
    {
        private readonly Func<T> _create;

        public PoolableStrategy(Func<T> create)
        {
            _create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public T Create()
        {
            var item = _create();
            item.OnCreated();
            return item;
        }

        public bool IsValid(T item) => true;

        public void OnRent(T item) => item.OnRented();

        public void OnReturn(T item) => item.OnReturned();

        public void OnDestroy(T item) => item.OnDestroying();
    }
}
