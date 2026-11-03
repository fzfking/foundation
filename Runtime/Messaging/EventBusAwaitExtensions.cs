using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public static class EventBusAwaitExtensions
    {
        public static async UniTask<TEvent> WaitForEventAsync<TEvent>(
            this IEventBus eventBus,
            Func<TEvent, bool> predicate = null,
            CancellationToken cancellationToken = default)
        {
            if (eventBus == null)
                throw new ArgumentNullException(nameof(eventBus));
            cancellationToken.ThrowIfCancellationRequested();

            var completion = new UniTaskCompletionSource<TEvent>();
            var handler = new PredicateHandler<TEvent>(predicate, completion);
            using var subscription = eventBus.Subscribe(handler);
            await using var registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken))
                : default;

            return await completion.Task;
        }

        private sealed class PredicateHandler<TEvent> : IEventHandler<TEvent>
        {
            private readonly Func<TEvent, bool> _predicate;
            private readonly UniTaskCompletionSource<TEvent> _completion;

            public PredicateHandler(Func<TEvent, bool> predicate, UniTaskCompletionSource<TEvent> completion)
            {
                _predicate = predicate;
                _completion = completion;
            }

            public UniTask HandleAsync(TEvent message, CancellationToken cancellationToken = default)
            {
                try
                {
                    if (_predicate == null || _predicate(message))
                        _completion.TrySetResult(message);
                }
                catch (Exception exception)
                {
                    _completion.TrySetException(exception);
                }

                return UniTask.CompletedTask;
            }
        }
    }
}
