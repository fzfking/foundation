using System.Collections.Generic;

namespace Foundation.UI
{
    public readonly struct UiArgs
    {
        private readonly IReadOnlyDictionary<string, object> _values;

        public static UiArgs Empty => default;

        public bool IsEmpty => _values == null || _values.Count == 0;

        public IReadOnlyDictionary<string, object> Values => _values;

        public UiArgs(IReadOnlyDictionary<string, object> values)
        {
            _values = values;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            if (_values != null && _values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }

            value = default;
            return false;
        }
    }
}
