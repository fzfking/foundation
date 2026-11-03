using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Foundation.Localization;

namespace Foundation.Content.Editor.Localization
{
    public static class LocalizationCsvReader
    {
        public const string KeyColumn = "Key";
        public const string ContextColumn = "Context";

        public static LocalizationDocument ReadFile(string path)
        {
            using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return Read(reader, path);
        }

        public static LocalizationDocument ReadText(string csv, string sourcePath = "localization.csv")
        {
            using var reader = new StringReader(csv);
            return Read(reader, sourcePath);
        }

        private static LocalizationDocument Read(TextReader textReader, string sourcePath)
        {
            var headerErrors = new List<string>();
            using var csv = CsvRecordReader.Create(textReader);

            var keyColumn = -1;
            var contextColumn = -1;
            var locales = new List<LocaleId>();
            var localeColumns = new List<int>();

            for (var column = 0; column < csv.FieldCount; column++)
            {
                var header = csv.GetName(column)?.Trim();
                if (string.Equals(header, KeyColumn, StringComparison.OrdinalIgnoreCase))
                {
                    if (keyColumn >= 0)
                        headerErrors.Add($"column '{header}' duplicates the reserved '{KeyColumn}' column.");
                    else
                        keyColumn = column;
                    continue;
                }

                if (string.Equals(header, ContextColumn, StringComparison.OrdinalIgnoreCase))
                {
                    if (contextColumn >= 0)
                        headerErrors.Add($"column '{header}' duplicates the reserved '{ContextColumn}' column.");
                    else
                        contextColumn = column;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(header))
                {
                    headerErrors.Add($"column {column + 1} has an empty header; expected a locale id.");
                    continue;
                }

                LocaleId locale;
                try
                {
                    locale = new LocaleId(header);
                }
                catch (ArgumentException)
                {
                    headerErrors.Add($"column '{header}' is not a valid locale id.");
                    continue;
                }

                // Locale equality is the runtime's, so 'pt-BR' and 'pt-br' cannot quietly become
                // two columns feeding one runtime locale.
                if (locales.Contains(locale))
                {
                    headerErrors.Add(
                        $"locale column '{header}' duplicates an earlier column that resolves to the same locale.");
                    continue;
                }

                locales.Add(locale);
                localeColumns.Add(column);
            }

            if (keyColumn < 0)
                headerErrors.Add($"required column '{KeyColumn}' is missing.");
            if (contextColumn < 0)
                headerErrors.Add($"required column '{ContextColumn}' is missing.");
            if (locales.Count == 0)
                headerErrors.Add("no locale columns found after the reserved columns.");

            var rows = new List<LocalizationRow>();
            if (keyColumn >= 0)
            {
                while (csv.Read())
                {
                    var key = csv.GetString(keyColumn)?.Trim() ?? string.Empty;
                    var context = contextColumn >= 0 ? csv.GetString(contextColumn) ?? string.Empty : string.Empty;
                    if (string.IsNullOrWhiteSpace(key) && IsRowEmpty(csv, localeColumns))
                        continue;

                    var values = new Dictionary<LocaleId, string>(locales.Count);
                    for (var index = 0; index < locales.Count; index++)
                    {
                        var text = csv.GetString(localeColumns[index]);
                        values[locales[index]] = string.IsNullOrEmpty(text) ? null : text;
                    }

                    rows.Add(new LocalizationRow(key, context, values, csv.RowNumber));
                }
            }

            headerErrors.AddRange(csv.Errors);
            return new LocalizationDocument(sourcePath, locales, rows, headerErrors);
        }

        private static bool IsRowEmpty(CsvRecordReader csv, IReadOnlyList<int> localeColumns)
        {
            for (var index = 0; index < localeColumns.Count; index++)
            {
                if (!string.IsNullOrWhiteSpace(csv.GetString(localeColumns[index])))
                    return false;
            }

            return true;
        }
    }
}
