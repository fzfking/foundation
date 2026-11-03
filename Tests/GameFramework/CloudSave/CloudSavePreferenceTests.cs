#if FOUNDATION_GOOGLE_PLAY_GAMES
using Foundation.GameFramework.CloudSave;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.GameFramework.Tests
{
    public sealed class CloudSavePreferenceTests
    {
        private const string Key = "Foundation.Tests.CloudSavePreference";

        [TearDown]
        public void TearDown() => PlayerPrefs.DeleteKey(Key);

        [Test]
        public void ANewPlayer_HasCloudSaveOn_UntilTheyTurnItOff()
        {
            PlayerPrefs.DeleteKey(Key);
            var preference = new PlayerPrefsCloudSavePreference(Key);

            Assert.That(preference.IsEnabled, Is.True);

            preference.SetEnabled(false);
            Assert.That(new PlayerPrefsCloudSavePreference(Key).IsEnabled, Is.False);
        }

        [Test]
        public void TheDefaultKey_IsTheOneEarlierBuildsWroteTheToggleUnder()
        {
            PlayerPrefs.SetInt("Foundation.GameFramework.CloudSave.Enabled", 0);
            try
            {
                Assert.That(new PlayerPrefsCloudSavePreference().IsEnabled, Is.False);
            }
            finally
            {
                PlayerPrefs.DeleteKey("Foundation.GameFramework.CloudSave.Enabled");
            }
        }
    }
}
#endif
