using System;
using UnityEngine;

namespace Foundation.Infrastructure.YooAsset
{
    public sealed class PlayerPrefsYooAssetVersionStore : IYooAssetVersionStore
    {
        private const string KeyPrefix = "Foundation.YooAsset.SyncedVersion.";

        public string Read(string packageName)
        {
            var version = PlayerPrefs.GetString(KeyOf(packageName), string.Empty);
            return string.IsNullOrEmpty(version) ? null : version;
        }

        public void Write(string packageName, string version)
        {
            if (string.IsNullOrEmpty(version))
                throw new ArgumentException("A version must not be empty.", nameof(version));

            PlayerPrefs.SetString(KeyOf(packageName), version);
            PlayerPrefs.Save();
        }

        private static string KeyOf(string packageName) => KeyPrefix + packageName;
    }
}
