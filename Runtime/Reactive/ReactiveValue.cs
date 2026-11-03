using System;
using System.Collections.Generic;

namespace Foundation.Reactive
{
    /// <summary>
    /// A value that publishes when it changes to a different one (by
    /// <see cref="EqualityComparer{T}.Default"/>). Subscribers get the current value on subscription, then each
    /// change.
    /// </summary>
    public sealed class ReactiveValue<T> : IReadOnlyReactiveValue<T>, IDisposable
    {
        private readonly EventStream<T> _changes = new();
        private T _value;

        public T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value))
                    return;

                _value = value;
                _changes.Publish(value);
            }
        }

        public ReactiveValue(T initialValue = default)
        {
            _value = initialValue;
        }

        public void Dispose() => _changes.Dispose();

        public IDisposable Subscribe(Action<T> onNext)
        {
            if (onNext == null)
                throw new ArgumentNullException(nameof(onNext));

            var subscription = _changes.Subscribe(onNext);
            try
            {
                onNext(_value);
            }
            catch
            {
                subscription.Dispose();
                throw;
            }

            return subscription;
        }
    }
}
