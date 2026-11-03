using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace Foundation.Reactive
{
    /// <summary>
    /// Owns a set of disposables and releases them together: the subscriptions of one view, one screen, one run.
    /// Main thread only. <see cref="Clear"/> releases what it holds and the group is used again; <see cref="Dispose"/>
    /// ends it for good, after which anything added is released at once. Items are released newest first, so a
    /// resource is gone before what it was built on. One that throws does not keep the others from being released;
    /// the failure is reported once all of them are.
    /// </summary>
    public sealed class DisposableGroup : IDisposable
    {
        private readonly List<IDisposable> _items = new();
        private bool _disposed;

        public int Count => _items.Count;
        public bool IsDisposed => _disposed;

        /// <summary>Takes ownership of <paramref name="item"/>; released at once when the group is already disposed.</summary>
        public T Add<T>(T item) where T : IDisposable
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            if (_disposed)
                item.Dispose();
            else
                _items.Add(item);

            return item;
        }

        /// <summary>Runs <paramref name="onDispose"/> when the group releases it.</summary>
        public void Add(Action onDispose)
        {
            if (onDispose == null)
                throw new ArgumentNullException(nameof(onDispose));

            Add(new ActionDisposable(onDispose));
        }

        /// <summary>Releases <paramref name="item"/> now and stops owning it; false if the group did not own it.</summary>
        public bool Remove(IDisposable item)
        {
            if (item == null || !_items.Remove(item))
                return false;

            item.Dispose();
            return true;
        }

        /// <summary>Releases everything held; the group stays usable.</summary>
        public void Clear()
        {
            List<Exception> failures = null;

            // Taken off the end one at a time so an item that adds or clears while being released is still handled.
            while (_items.Count > 0)
            {
                var last = _items.Count - 1;
                var item = _items[last];
                _items.RemoveAt(last);
                try
                {
                    item.Dispose();
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("Several disposables failed to dispose.", failures);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Clear();
        }

        private sealed class ActionDisposable : IDisposable
        {
            private Action _onDispose;

            public ActionDisposable(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                var onDispose = _onDispose;
                _onDispose = null;
                onDispose?.Invoke();
            }
        }
    }
}
