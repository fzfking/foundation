using System;
using System.Collections.Generic;

namespace Foundation.Localization
{
    public sealed class LocalizationTable
    {
        private readonly Dictionary<string, string> _entries;

        public LocaleId Locale { get; }

        public int Count => _entries.Count;

        public IReadOnlyDictionary<string, string> Entries => _entries;

        public LocalizationTable(LocaleId locale, IReadOnlyDictionary<string, string> entries)
        {
            Locale = locale;
            _entries = new(
                entries?.Count ?? 0,
                StringComparer.Ordinal);
            foreach (var pair in entries ?? new Dictionary<string, string>())
                _entries[pair.Key] = pair.Value;
        }

        public bool TryGet(LocalizationKey key, out string template) =>
            _entries.TryGetValue(key.Value, out template);

        public static LocalizationTable FromData(LocalizationTableData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (string.IsNullOrWhiteSpace(data.Locale))
                throw new InvalidOperationException("Localization table has no locale.");

            var keys = data.Keys ?? Array.Empty<string>();
            var values = data.Values ?? Array.Empty<string>();
            if (keys.Length != values.Length)
                throw new InvalidOperationException(
                    $"Localization table '{data.Locale}' has {keys.Length} keys and {values.Length} values.");

            var entries = new Dictionary<string, string>(keys.Length, StringComparer.Ordinal);
            for (var index = 0; index < keys.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(keys[index]))
                    throw new InvalidOperationException(
                        $"Localization table '{data.Locale}' contains an empty key.");

                if (!entries.TryAdd(keys[index], values[index]))
                    throw new InvalidOperationException(
                        $"Localization table '{data.Locale}' contains duplicate key '{keys[index]}'.");
            }

            return new LocalizationTable(new LocaleId(data.Locale), entries);
        }
    }
}
