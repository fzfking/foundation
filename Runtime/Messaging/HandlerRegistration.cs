using System;
using System.Collections.Generic;

namespace Foundation.Messaging
{
    // Removes only the handler this registration added: if another took the slot after it, disposing leaves that
    // one alone.
    internal sealed class HandlerRegistration<TKey> : IDisposable
    {
        private readonly Dictionary<TKey, object> _handlers;
        private readonly TKey _key;
        private object _handler;

        public HandlerRegistration(Dictionary<TKey, object> handlers, TKey key, object handler)
        {
            _handlers = handlers;
            _key = key;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_handler == null)
                return;

            if (_handlers.TryGetValue(_key, out var current) && ReferenceEquals(current, _handler))
                _handlers.Remove(_key);
            _handler = null;
        }
    }
}
