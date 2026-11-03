using System;
using Foundation.GameFramework.Firebase;
using Cysharp.Threading.Tasks;
using UnityEngine;
using FirebaseCrashlytics = Firebase.Crashlytics.Crashlytics;

namespace Foundation.GameFramework.Crashlytics.Firebase
{
    /// <summary>
    /// Switches Crashlytics reporting on and off, which is how a project applies the player's consent. Once
    /// Firebase is up and collection is on, crashes and logged exceptions are reported by themselves; nothing
    /// needs forwarding.
    /// </summary>
    public static class FirebaseCrashlyticsCollection
    {
        public static async UniTask SetEnabledAsync(bool enabled)
        {
            if (!await FirebaseActivationCore.EnsureReadyAsync())
                return;

            try
            {
                FirebaseCrashlytics.IsCrashlyticsCollectionEnabled = enabled;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Firebase Crashlytics collection could not be switched: {exception.Message}");
            }
        }
    }
}
