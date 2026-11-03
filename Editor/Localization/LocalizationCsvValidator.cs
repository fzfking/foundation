using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Foundation.Content.Authoring;
using Foundation.Localization;

namespace Foundation.Content.Editor.Localization
{
    public sealed class LocalizationCsvValidator : IConfigValidator
    {
        private readonly LocaleId _fallback;

        public LocalizationCsvValidator(LocaleId fallback)
        {
            _fallback = fallback;
        }

        public IEnumerable<ConfigBuildError> Validate(
            IConfigAuthoringSource source,
            IReadOnlyList<ConfigExportGroup> groups)
        {
            if (source is not LocalizationCsvSource csvSource)
                yield break;

            var document = csvSource.Document;
            var sourceName = Path.GetFileName(document.SourcePath);

            foreach (var headerError in document.HeaderErrors)
                yield return Error(sourceName, headerError);

            var fallback = _fallback;
            if (!document.HasLocale(fallback))
            {
                yield return Error(
                    sourceName,
                    $"the canonical locale '{fallback}' has no column; "
                    + $"columns are: {string.Join(", ", document.Locales.Select(locale => locale.Value))}.");
                yield break;
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in document.Rows)
            {
                var key = row.Key?.Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    yield return Error(sourceName, $"row {row.RowNumber}: entry has an empty key.");
                    continue;
                }

                if (!IsValidKey(key))
                    yield return Error(
                        sourceName,
                        $"row {row.RowNumber}: key '{key}' is invalid: use letters, digits, '.', '-' and '_' only.",
                        key);

                if (!seenKeys.Add(key))
                    yield return Error(
                        sourceName,
                        $"row {row.RowNumber}: key '{key}' is defined more than once.",
                        key);

                foreach (var error in ValidateRow(sourceName, document, row, key, fallback))
                    yield return error;
            }
        }

        private static IEnumerable<ConfigBuildError> ValidateRow(
            string sourceName,
            LocalizationDocument document,
            LocalizationRow row,
            string key,
            LocaleId fallback)
        {
            var canonical = row.GetValue(fallback);
            if (canonical == null)
            {
                yield return Error(
                    sourceName,
                    $"row {row.RowNumber}: key '{key}' has no text for the canonical locale '{fallback}'.",
                    key);
            }

            IReadOnlyList<string> canonicalPlaceholders = Array.Empty<string>();
            if (canonical != null
                && !LocalizationPlaceholders.TryParse(canonical, out canonicalPlaceholders, out var canonicalError))
            {
                yield return Error(
                    sourceName,
                    $"row {row.RowNumber}: key '{key}' locale '{fallback}': malformed format ({canonicalError}).",
                    key);
                canonicalPlaceholders = Array.Empty<string>();
                canonical = null;
            }

            foreach (var locale in document.Locales)
            {
                var text = row.GetValue(locale);
                if (text == null)
                    continue;

                if (!LocalizationPlaceholders.TryParse(text, out var placeholders, out var error))
                {
                    yield return Error(
                        sourceName,
                        $"row {row.RowNumber}: key '{key}' locale '{locale}': malformed format ({error}).",
                        key);
                    continue;
                }

                if (locale == fallback || canonical == null)
                    continue;

                var missing = canonicalPlaceholders.Where(name => !placeholders.Contains(name)).ToArray();
                var extra = placeholders.Where(name => !canonicalPlaceholders.Contains(name)).ToArray();
                if (missing.Length == 0 && extra.Length == 0)
                    continue;

                var details = new List<string>();
                if (missing.Length > 0)
                    details.Add("missing " + string.Join(", ", missing.Select(name => "{" + name + "}")));
                if (extra.Length > 0)
                    details.Add("unexpected " + string.Join(", ", extra.Select(name => "{" + name + "}")));

                yield return Error(
                    sourceName,
                    $"row {row.RowNumber}: key '{key}' locale '{locale}': "
                    + $"placeholders differ from '{fallback}' ({string.Join("; ", details)}).",
                    key);
            }
        }

        private static bool IsValidKey(string key)
        {
            foreach (var character in key)
            {
                if (!char.IsLetterOrDigit(character) && character != '.' && character != '-' && character != '_')
                    return false;
            }

            return true;
        }

        private static ConfigBuildError Error(string sourceName, string message, string key = null) =>
            new(sourceName, "localization", ConfigBuildErrorKind.Invalid, message, key);
    }
}
