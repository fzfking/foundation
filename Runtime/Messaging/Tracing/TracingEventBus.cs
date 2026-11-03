#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Threading;
using Foundation.Diagnostics;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging.Tracing
{
    public sealed class TracingEventBus : IEventBus
    {
        private readonly IEventBus _inner;
        private readonly IOperationTracer _tracer;

        public TracingEventBus(IEventBus inner, IOperationTracer tracer)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            return _inner.Subscribe(new TracedHandler<TEvent>(handler, _tracer));
        }

        public UniTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
        {
            if (!_tracer.IsEnabled(OperationTraceCategory.Event))
                return _inner.PublishAsync(message, cancellationToken);

            var scope = _tracer.Begin(OperationTraceCategory.Event, "Event", typeof(TEvent).Name);
            return TracedOperation.RunAsync(scope, () => _inner.PublishAsync(message, cancellationToken));
        }

        private sealed class TracedHandler<TEvent> : IEventHandler<TEvent>
        {
            private readonly IEventHandler<TEvent> _handler;
            private readonly IOperationTracer _tracer;

            public TracedHandler(IEventHandler<TEvent> handler, IOperationTracer tracer)
            {
                _handler = handler;
                _tracer = tracer;
            }

            public UniTask HandleAsync(TEvent message, CancellationToken cancellationToken = default)
            {
                if (!_tracer.IsEnabled(OperationTraceCategory.Subscriber))
                    return _handler.HandleAsync(message, cancellationToken);

                var scope = _tracer.Begin(
                    OperationTraceCategory.Subscriber,
                    "Subscriber",
                    _handler.GetType().Name);
                return TracedOperation.RunAsync(scope, () => _handler.HandleAsync(message, cancellationToken));
            }
        }
    }
}
#endif
