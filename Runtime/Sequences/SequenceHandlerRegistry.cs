using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Sequences
{
    /// <summary>Owned by the consumer's DI scope; no static discovery or gameplay references.</summary>
    public sealed class SequenceHandlerRegistry
    {
        private readonly Dictionary<string, Func<SequenceContext, CancellationToken, UniTask>> _actions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<SequenceContext, CancellationToken, UniTask>> _waits = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Func<SequenceContext, bool>> _conditions = new(StringComparer.Ordinal);

        public SequenceHandlerRegistry(IEnumerable<ISequenceHandlerProvider> providers = null)
        {
            if (providers == null)
                return;
            foreach (var provider in providers)
                provider.Register(this);
        }

        public void RegisterAction(string key, Func<SequenceContext, CancellationToken, UniTask> handler)
            => Add(_actions, key, handler);

        public void RegisterWait(string key, Func<SequenceContext, CancellationToken, UniTask> handler)
            => Add(_waits, key, handler);

        public void RegisterCondition(string key, Func<SequenceContext, bool> handler)
            => Add(_conditions, key, handler);

        public UniTask ExecuteAsync(string key, SequenceContext context, CancellationToken token)
            => Get(_actions, key)(context, token);

        public UniTask WaitAsync(string key, SequenceContext context, CancellationToken token)
            => Get(_waits, key)(context, token);

        public bool Evaluate(string key, SequenceContext context) => Get(_conditions, key)(context);

        private static void Add<T>(Dictionary<string, T> handlers, string key, T handler) where T : class
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A sequence handler needs a key.", nameof(key));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));
            if (!handlers.TryAdd(key, handler))
                throw new InvalidOperationException($"Sequence handler '{key}' is registered more than once.");
        }

        private static T Get<T>(Dictionary<string, T> handlers, string key)
            => handlers.TryGetValue(key ?? string.Empty, out var handler)
                ? handler
                : throw new InvalidOperationException($"Sequence handler '{key}' is not registered.");
    }
}
