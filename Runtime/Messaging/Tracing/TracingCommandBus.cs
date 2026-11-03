#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Threading;
using Foundation.Diagnostics;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging.Tracing
{
    public sealed class TracingCommandBus : ICommandBus
    {
        private readonly ICommandBus _inner;
        private readonly IOperationTracer _tracer;

        public TracingCommandBus(ICommandBus inner, IOperationTracer tracer)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        public UniTask SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
        {
            if (!_tracer.IsEnabled(OperationTraceCategory.Command))
                return _inner.SendAsync(command, cancellationToken);

            var scope = _tracer.Begin(OperationTraceCategory.Command, "Command", typeof(TCommand).Name);
            return TracedOperation.RunAsync(scope, () => _inner.SendAsync(command, cancellationToken));
        }
    }
}
#endif
