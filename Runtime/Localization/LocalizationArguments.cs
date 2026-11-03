using System;
using System.Collections.Generic;

namespace Foundation.Localization
{
    public readonly struct LocalizationArguments
    {
        public static LocalizationArguments Empty => default;
        
        private readonly IReadOnlyDictionary<string, object> _values;
        public bool IsEmpty => _values == null || _values.Count == 0;
        public IReadOnlyDictionary<string, object> Values => _values;
        
        public LocalizationArguments(IReadOnlyDictionary<string, object> values)
        {
            _values = values ?? throw new ArgumentNullException(nameof(values));
        }
    }
}
