using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface IQueryHandlerRegistry
    {
        /// <summary>Registers the handler of <typeparamref name="TQuery"/>; disposing the result removes it again.</summary>
        IDisposable RegisterHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> handler);
    }

    /// <summary>
    /// Answers a query from its one handler, looked up by query and result type. A missing handler fails the
    /// call instead of leaving the caller waiting.
    /// </summary>
    public sealed class QueryBus : IQueryBus, IQueryHandlerRegistry
    {
        private readonly Dictionary<QueryKey, object> _handlers = new();

        public IDisposable RegisterHandler<TQuery, TResult>(IQueryHandler<TQuery, TResult> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var key = new QueryKey(typeof(TQuery), typeof(TResult));
            if (!_handlers.TryAdd(key, handler))
            {
                throw new InvalidOperationException(
                    $"A query handler for '{typeof(TQuery).FullName}' with result '{typeof(TResult).FullName}' is already registered.");
            }

            return new HandlerRegistration<QueryKey>(_handlers, key, handler);
        }

        public UniTask<TResult> QueryAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        {
            var key = new QueryKey(typeof(TQuery), typeof(TResult));
            if (!_handlers.TryGetValue(key, out var handler))
            {
                throw new InvalidOperationException(
                    $"No query handler is registered for '{typeof(TQuery).FullName}' with result '{typeof(TResult).FullName}'.");
            }

            return ((IQueryHandler<TQuery, TResult>)handler).HandleAsync(query, cancellationToken);
        }

        private readonly struct QueryKey : IEquatable<QueryKey>
        {
            private readonly Type _queryType;
            private readonly Type _resultType;

            public QueryKey(Type queryType, Type resultType)
            {
                _queryType = queryType;
                _resultType = resultType;
            }

            public bool Equals(QueryKey other) => _queryType == other._queryType && _resultType == other._resultType;

            public override bool Equals(object obj) => obj is QueryKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(_queryType, _resultType);
        }
    }
}
