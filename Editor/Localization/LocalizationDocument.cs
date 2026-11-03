using System;
using System.Collections.Generic;
using Foundation.Localization;

namespace Foundation.Content.Editor.Localization
{
    public sealed class LocalizationRow
    {
        private readonly Dictionary<LocaleId, string> _values;

        public string Key { get; }

        public string Context { get; }

        public int RowNumber { get; }

        public IReadOnlyDictionary<LocaleId, string> Values => _values;

        public LocalizationRow(
            string key,
            string context,
            IReadOnlyDictionary<LocaleId, string> values,
            int rowNumber)
        {
            Key = key;
            Context = context;
            RowNumber = rowNumber;
            _values = new(values?.Count ?? 0);
            foreach (var pair in values ?? new Dictionary<LocaleId, string>())
                _values[pair.Key] = pair.Value;
        }

        public string GetValue(LocaleId locale) =>
            _values.TryGetValue(locale, out var value) && !string.IsNullOrEmpty(value) ? value : null;
    }

    public sealed class LocalizationDocument
    {
        public string SourcePath { get; }

        public IReadOnlyList<LocaleId> Locales { get; }

        public IReadOnlyList<LocalizationRow> Rows { get; }

        public IReadOnlyList<string> HeaderErrors { get; }

        public LocalizationDocument(
            string sourcePath,
            IReadOnlyList<LocaleId> locales,
            IReadOnlyList<LocalizationRow> rows,
            IReadOnlyList<string> headerErrors = null)
        {
            SourcePath = sourcePath ?? string.Empty;
            Locales = locales ?? Array.Empty<LocaleId>();
            Rows = rows ?? Array.Empty<LocalizationRow>();
            HeaderErrors = headerErrors ?? Array.Empty<string>();
        }

        public bool HasLocale(LocaleId locale)
        {
            for (var index = 0; index < Locales.Count; index++)
            {
                if (Locales[index] == locale)
                    return true;
            }

            return false;
        }
    }
}
