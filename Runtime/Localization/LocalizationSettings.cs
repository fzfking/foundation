using System;
using System.Collections.Generic;

namespace Foundation.Localization
{
    public sealed class LocalizationSettings
    {
        private readonly LocaleId[] _supported;

        public IReadOnlyList<LocaleId> SupportedLocales => _supported;

        public LocaleId FallbackLocale { get; }

        public LocalizationSettings(IEnumerable<LocaleId> supportedLocales, LocaleId fallbackLocale)
        {
            if (supportedLocales == null)
                throw new ArgumentNullException(nameof(supportedLocales));

            var list = new List<LocaleId>();
            foreach (var locale in supportedLocales)
            {
                if (locale.IsEmpty)
                    throw new ArgumentException("Supported locales must not contain an empty locale.", nameof(supportedLocales));
                if (list.Contains(locale))
                    throw new ArgumentException($"Locale '{locale}' is listed more than once.", nameof(supportedLocales));

                list.Add(locale);
            }

            if (list.Count == 0)
                throw new ArgumentException("At least one supported locale is required.", nameof(supportedLocales));
            if (!list.Contains(fallbackLocale))
                throw new ArgumentException(
                    $"Fallback locale '{fallbackLocale}' is not one of the supported locales.",
                    nameof(fallbackLocale));

            _supported = list.ToArray();
            FallbackLocale = fallbackLocale;
        }

        public bool IsSupported(LocaleId locale)
        {
            foreach (var localeId in _supported)
            {
                if (localeId == locale)
                    return true;
            }

            return false;
        }

        public bool TryResolveSupported(LocaleId requested, out LocaleId resolved)
        {
            if (!requested.IsEmpty)
            {
                if (IsSupported(requested))
                {
                    resolved = requested;
                    return true;
                }

                var language = requested.Language;
                if (IsSupported(language))
                {
                    resolved = language;
                    return true;
                }

                for (var index = 0; index < _supported.Length; index++)
                {
                    if (_supported[index].Language == language)
                    {
                        resolved = _supported[index];
                        return true;
                    }
                }
            }

            resolved = default;
            return false;
        }
    }
}
