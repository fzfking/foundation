using System;

namespace Foundation.Reactive
{
    /// <summary>A stream of values pushed to subscribers. Dispose the returned handle to unsubscribe.</summary>
    public interface IEventStream<out T>
    {
        IDisposable Subscribe(Action<T> onNext);
    }

    /// <summary>
    /// A value that announces its changes. Subscribing delivers the current value at once, then every change,
    /// so no separate initial read is needed.
    /// </summary>
    public interface IReadOnlyReactiveValue<T> : IEventStream<T>
    {
        T Value { get; }
    }
}
