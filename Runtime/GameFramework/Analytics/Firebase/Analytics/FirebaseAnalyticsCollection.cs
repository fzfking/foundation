using Cysharp.Threading.Tasks;
using Firebase.Analytics;

namespace Foundation.GameFramework.Analytics.Firebase
{
    /// <summary>Switches Firebase Analytics collection on and off, which is how a project applies the player's analytics consent.</summary>
    public static class FirebaseAnalyticsCollection
    {
    /// <summary>Foundation's event gate; it is enabled only after the SDK accepts the collection setting.</summary>
        public static bool IsEnabled { get; private set; }

        public static async UniTask SetEnabledAsync(bool enabled)
        {
            if (!enabled)
            {
                // Off takes effect at once; waiting for the app would let events slip through meanwhile.
                IsEnabled = false;
                if (!await FirebaseActivation.EnsureReadyAsync())
                    return;
                Apply(false);
                return;
            }

            if (!await FirebaseActivation.EnsureReadyAsync())
                return;

            Apply(true);
            IsEnabled = true;
        }

        private static void Apply(bool enabled)
            => FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled);
    }
}
