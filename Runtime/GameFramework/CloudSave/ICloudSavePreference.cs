using UnityEngine;
using System;

namespace Foundation.GameFramework.CloudSave
{
    /// <summary>
    /// The player's cloud-save toggle. It is a preference, not an account action: sign-in still runs on every
    /// start, but cloud saving stays off until the player turns it back on.
    /// </summary>
    public interface ICloudSavePreference
    {
        bool IsEnabled { get; }

        void SetEnabled(bool enabled);
    }

    /// <summary>Keeps the toggle in <see cref="PlayerPrefs"/> under a key the game chooses. On until the player turns it off.</summary>
    public sealed class PlayerPrefsCloudSavePreference : ICloudSavePreference
    {
        /// <summary>The key earlier builds stored the toggle under, so a player's choice survives the move to a configurable key.</summary>
        public const string DefaultKey = "Foundation.GameFramework.CloudSave.Enabled";

        private readonly string _key;

        public bool IsEnabled => PlayerPrefs.GetInt(_key, 1) == 1;

        public PlayerPrefsCloudSavePreference(string key = DefaultKey)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("The preference key must not be empty.", nameof(key));

            _key = key;
        }

        public void SetEnabled(bool enabled)
        {
            PlayerPrefs.SetInt(_key, enabled ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
