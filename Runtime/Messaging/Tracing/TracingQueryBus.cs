#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Threading;
using Foundation.Diagnostics;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging.Tracing
{
    public sealed class TracingQueryBus : IQueryBus
    {
        private readonly IQueryBus _inner;
        private readonly IOperationTracer _tracer;

        public TracingQueryBus(IQueryBus inner, IOperationTracer tracer)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _tracer = tracer ?? throw new ArgumentNullException(nameof(tracer));
        }

        public UniTask<TResult> QueryAsync<TQuery, TResult>(
            TQuery query,
            CancellationToken cancellationToken = default)
        {
            if (!_tracer.IsEnabled(OperationTraceCategory.Query))
                return _inner.QueryAsync<TQuery, TResult>(query, cancellationToken);

            var scope = _tracer.Begin(OperationTraceCategory.Query, "Query", typeof(TQuery).Name);
            return TracedOperation.RunAsync(
                scope,
                () => _inner.QueryAsync<TQuery, TResult>(query, cancellationToken));
        }
    }
}
#endif
