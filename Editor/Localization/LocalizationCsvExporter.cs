using System;
using System.Collections.Generic;
using System.Linq;
using Foundation.Content.Authoring;
using Foundation.Localization;

namespace Foundation.Content.Editor.Localization
{
    public sealed class LocalizationCsvExporter : IConfigExporter
    {
        public const string FileNamePrefix = "localization_";

        public Type SourceType => typeof(LocalizationCsvSource);

        public static string FileNameFor(LocaleId locale) => FileNamePrefix + locale.Value + ".bytes";

        public IReadOnlyList<ConfigExportGroup> Export(IConfigAuthoringSource source)
        {
            var document = ((LocalizationCsvSource)source).Document;
            var rows = document.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Key))
                .OrderBy(row => row.Key.Trim(), StringComparer.Ordinal)
                .ToArray();

            var groups = new List<ConfigExportGroup>(document.Locales.Count);
            foreach (var locale in document.Locales)
            {
                var keys = new List<string>(rows.Length);
                var values = new List<string>(rows.Length);
                foreach (var row in rows)
                {
                    var text = row.GetValue(locale);
                    // An untranslated cell produces no row at all: the runtime falls back instead
                    // of showing an empty string that looks translated.
                    if (text == null)
                        continue;

                    keys.Add(row.Key.Trim());
                    values.Add(text);
                }

                groups.Add(new ConfigExportGroup<LocalizationTableData>(
                    FileNameFor(locale),
                    new LocalizationTableData
                    {
                        Locale = locale.Value,
                        Keys = keys.ToArray(),
                        Values = values.ToArray()
                    },
                    keys));
            }

            return groups;
        }
    }
}
