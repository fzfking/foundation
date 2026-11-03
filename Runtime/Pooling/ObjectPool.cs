using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Foundation.Pooling
{
    public interface IObjectPool<T>
    {
        T Rent();
        void Return(T item);
        void Clear();
        int CountActive { get; }
        int CountInactive { get; }
    }

    public sealed class ObjectPool<T> : IObjectPool<T>
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly Action<T> _onDestroy;
        private readonly int _maxSize;
        private readonly Stack<T> _inactive = new();

        // The pooled objects themselves, by reference, so one returned twice is caught instead of being handed out to two
        // callers later. Value types are copies, so there is nothing to track for them.
        private readonly HashSet<T> _inactiveSet = typeof(T).IsValueType 
            ? null 
            : new HashSet<T>(ReferenceComparer.Instance);

        private int _countActive;
        public int CountActive => _countActive;
        public int CountInactive => _inactive.Count;

        public ObjectPool(
            Func<T> factory,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            Action<T> onDestroy = null,
            int maxSize = int.MaxValue)
        {
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onRent = onRent;
            _onReturn = onReturn;
            _onDestroy = onDestroy;
            _maxSize = maxSize > 0 ? maxSize : throw new ArgumentOutOfRangeException(nameof(maxSize));
        }

        public T Rent()
        {
            var item = _inactive.Count > 0 ? _inactive.Pop() : _factory();
            _inactiveSet?.Remove(item);
            _countActive++;
            _onRent?.Invoke(item);
            return item;
        }

        public void Return(T item)
        {
            if (_inactiveSet != null)
            {
                if (item == null)
                    throw new ArgumentNullException(nameof(item));
                if (_inactiveSet.Contains(item))
                    throw new InvalidOperationException("This object was already returned to the pool.");
            }

            _countActive--;
            _onReturn?.Invoke(item);

            if (_inactive.Count >= _maxSize)
            {
                _onDestroy?.Invoke(item);
                return;
            }

            _inactive.Push(item);
            _inactiveSet?.Add(item);
        }

        public void Clear()
        {
            if (_onDestroy != null)
            {
                foreach (var item in _inactive)
                    _onDestroy(item);
            }

            _inactive.Clear();
            _inactiveSet?.Clear();
        }

        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceComparer Instance = new();
            public bool Equals(T left, T right) => ReferenceEquals(left, right);
            public int GetHashCode(T item) => RuntimeHelpers.GetHashCode(item);
        }
    }
}
