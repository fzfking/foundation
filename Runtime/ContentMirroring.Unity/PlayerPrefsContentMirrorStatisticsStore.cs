using System;
using System.Globalization;
using Foundation.ContentMirroring;
using UnityEngine;

namespace Foundation.Infrastructure.ContentMirroring
{
    public sealed class PlayerPrefsContentMirrorStatisticsStore : IContentMirrorStatisticsStore
    {
        private const string KeyPrefix = "Foundation.ContentMirror.";

        public bool TryGetBytesPerSecond(string mirrorId, DateTimeOffset now, TimeSpan maximumAge, out double bytesPerSecond)
        {
            var key = KeyFor(mirrorId);
            bytesPerSecond = 0d;
            if (!PlayerPrefs.HasKey(key + ".speed")
                || !PlayerPrefs.HasKey(key + ".measuredAt")
                || !double.TryParse(PlayerPrefs.GetString(key + ".speed"), NumberStyles.Float, CultureInfo.InvariantCulture, out bytesPerSecond)
                || !long.TryParse(PlayerPrefs.GetString(key + ".measuredAt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var measuredAt))
                return false;

            if (double.IsNaN(bytesPerSecond) || double.IsInfinity(bytesPerSecond) || bytesPerSecond <= 0d)
                return false;

            DateTimeOffset measuredAtUtc;
            try
            {
                measuredAtUtc = DateTimeOffset.FromUnixTimeSeconds(measuredAt);
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }

            var age = now - measuredAtUtc;
            if (age < TimeSpan.Zero || age > maximumAge)
                return false;

            return true;
        }

        public void SaveBytesPerSecond(string mirrorId, double bytesPerSecond, DateTimeOffset measuredAt)
        {
            if (bytesPerSecond <= 0d)
            {
                Remove(mirrorId);
                return;
            }

            var key = KeyFor(mirrorId);
            PlayerPrefs.SetString(key + ".speed", bytesPerSecond.ToString("R", CultureInfo.InvariantCulture));
            PlayerPrefs.SetString(key + ".measuredAt", measuredAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
            PlayerPrefs.Save();
        }

        public void Remove(string mirrorId)
        {
            var key = KeyFor(mirrorId);
            PlayerPrefs.DeleteKey(key + ".speed");
            PlayerPrefs.DeleteKey(key + ".measuredAt");
            PlayerPrefs.Save();
        }

        private static string KeyFor(string mirrorId) => KeyPrefix + Uri.EscapeDataString(mirrorId);
    }
}
