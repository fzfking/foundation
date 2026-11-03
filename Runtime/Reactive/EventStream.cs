using System;
using UnityEngine;

namespace Foundation.Reactive
{
    /// <summary>
    /// The publishing side of an <see cref="IEventStream{T}"/>. Main thread only. A subscriber that throws is
    /// reported and does not stop the others; subscribing or unsubscribing during a publish is safe.
    /// </summary>
    public sealed class EventStream<T> : IEventStream<T>, IDisposable
    {
        private Action<T>[] _subscribers = Array.Empty<Action<T>>();
        private bool _disposed;

        public int SubscriberCount => _subscribers.Length;

        public void Dispose()
        {
            _disposed = true;
            _subscribers = Array.Empty<Action<T>>();
        }

        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            if (_disposed)
                return EmptyDisposable.Instance;

            var updated = new Action<T>[_subscribers.Length + 1];
            Array.Copy(_subscribers, updated, _subscribers.Length);
            updated[_subscribers.Length] = onNext;
            _subscribers = updated;
            return new Subscription(this, onNext);
        }

        public void Publish(T value)
        {
            var snapshot = _subscribers;
            foreach (var action in snapshot)
            {
                try
                {
                    action(value);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }

        private void Unsubscribe(Action<T> onNext)
        {
            var current = _subscribers;
            var position = Array.IndexOf(current, onNext);
            if (position < 0)
                return;

            var updated = new Action<T>[current.Length - 1];
            Array.Copy(current, 0, updated, 0, position);
            Array.Copy(current, position + 1, updated, position, current.Length - position - 1);
            _subscribers = updated;
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action<T> _handler;
            private EventStream<T> _owner;

            public Subscription(EventStream<T> owner, Action<T> handler)
            {
                _owner = owner;
                _handler = handler;
            }

            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                owner?.Unsubscribe(_handler);
            }
        }
    }

    internal sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
