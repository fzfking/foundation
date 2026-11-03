using System;
using System.Collections.Generic;

namespace Foundation.Pooling
{
    /// <summary>
    /// The views of one kind of entity, keyed by id and backed by a pool. A frame brackets its reads with
    /// <see cref="BeginSync"/> and <see cref="EndSync"/>: every id it <see cref="Acquire"/>s keeps (or gets)
    /// a view, every other view returns to the pool. Allocation-free after warm-up. Main thread only; classes
    /// only, so generic code is shared on IL2CPP and HybridCLR.
    /// </summary>
    public sealed class KeyedViewSet<TKey, TView>
        where TView : class
    {
        private readonly IPool<TView> _pool;
        private readonly Action<TView> _onReleased;
        private readonly Dictionary<TKey, TView> _views;
        private readonly HashSet<TKey> _seen;
        private readonly List<TKey> _stale = new();

        public int Count => _views.Count;

        public KeyedViewSet(IPool<TView> pool, Action<TView> onReleased = null, IEqualityComparer<TKey> comparer = null)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _onReleased = onReleased;
            _views = new(comparer);
            _seen = new(comparer);
        }

        /// <summary>Starts a pass: no id counts as present until it is acquired again.</summary>
        public void BeginSync() => _seen.Clear();

        /// <summary>
        /// Returns the view of <paramref name="key"/>, renting one on first sight, and marks the id present
        /// in this pass. Null when the pool is exhausted; the id is then not shown.
        /// </summary>
        public TView Acquire(TKey key, out bool created)
        {
            if (_views.TryGetValue(key, out var view))
            {
                created = false;
                _seen.Add(key);
                return view;
            }

            created = true;
            if (!_pool.TryRent(out view) || view == null)
            {
                created = false;
                return null;
            }

            _views.Add(key, view);
            _seen.Add(key);
            return view;
        }

        public bool TryGet(TKey key, out TView view) => _views.TryGetValue(key, out view);

        /// <summary>Ends a pass: views of ids that were not acquired since <see cref="BeginSync"/> go back to the pool.</summary>
        public void EndSync()
        {
            _stale.Clear();
            foreach (var pair in _views)
                if (!_seen.Contains(pair.Key))
                    _stale.Add(pair.Key);

            foreach (var key in _stale)
                Release(key);

            _stale.Clear();
        }

        /// <summary>
        /// Ends a pass like <see cref="EndSync()"/> but hands the views of vanished ids to the caller, for
        /// views that play a leaving animation; give them back with <see cref="Recycle"/> afterwards. They
        /// are appended to <paramref name="retired"/>.
        /// </summary>
        public void EndSync(List<TView> retired)
        {
            if (retired == null)
                throw new ArgumentNullException(nameof(retired));

            _stale.Clear();
            foreach (var pair in _views)
                if (!_seen.Contains(pair.Key))
                    _stale.Add(pair.Key);

            foreach (var key in _stale)
            {
                retired.Add(_views[key]);
                _views.Remove(key);
            }

            _stale.Clear();
        }

        public void Recycle(TView view)
        {
            if (view == null)
                return;

            _onReleased?.Invoke(view);
            _pool.Return(view);
        }

        public void Clear()
        {
            _stale.Clear();
            foreach (var key in _views.Keys)
                _stale.Add(key);

            foreach (var key in _stale)
                Release(key);

            _stale.Clear();
            _seen.Clear();
        }

        private void Release(TKey key)
        {
            if (!_views.Remove(key, out var view))
                return;

            _onReleased?.Invoke(view);
            _pool.Return(view);
        }
    }
}
