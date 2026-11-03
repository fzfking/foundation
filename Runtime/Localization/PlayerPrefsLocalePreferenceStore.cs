using UnityEngine;

namespace Foundation.Localization
{
    /// <summary>
    /// Persists the player's chosen locale on the device through PlayerPrefs. It is deliberately apart from the segmented game
    /// save: the locale must be readable before content, config and the save exist, at the very start of boot, which is also
    /// why <see cref="TryLoadSavedLocale"/> needs no instance.
    /// </summary>
    public sealed class PlayerPrefsLocalePreferenceStore : ILocalePreferenceStore
    {
        public const string DefaultKey = "settings.locale";

        private readonly string _key;

        public PlayerPrefsLocalePreferenceStore(string key = DefaultKey)
        {
            _key = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
        }

        public static bool TryLoadSavedLocale(out LocaleId locale, string key = DefaultKey)
        {
            if (PlayerPrefs.HasKey(key))
            {
                var value = PlayerPrefs.GetString(key);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    locale = new LocaleId(value);
                    return true;
                }
            }

            locale = default;
            return false;
        }

        public bool TryLoad(out LocaleId locale)
            => TryLoadSavedLocale(out locale, _key);

        public void Save(LocaleId locale)
        {
            PlayerPrefs.SetString(_key, locale.Value);
            PlayerPrefs.Save();
        }
    }
}
