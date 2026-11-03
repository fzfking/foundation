using System;
using Io.AppMetrica;
using UnityEngine;

namespace Foundation.GameFramework.Analytics.AppMetrica
{
    /// <summary>
    /// The only place that should call <c>Io.AppMetrica.AppMetrica.Activate</c>. It builds
    /// <see cref="AppMetricaConfig"/> with this module's defaults, so a project's consent gate only decides when
    /// to activate.
    /// Both crash-reporting flags default on: AppMetrica bundles crash reporting with analytics, so
    /// activating this backend satisfies the project's crash reporting.
    /// <see cref="AppMetricaUnhandledExceptionReporter"/> adds managed C# exceptions that native crash capture
    /// does not see.
    /// </summary>
    public static class AppMetricaActivation
    {
        private static bool _activatedWithKey;
        private static bool _sendingEnabled = true;

        /// <summary>Whether this process activated AppMetrica with the project's own API key.</summary>
        public static bool IsActivatedWithKey => _activatedWithKey;

        /// <summary>
        /// Whether the project's reports may go out: activated with the key (done by the consent gate after
        /// consent) and not switched off since (revoked consent).
        /// </summary>
        public static bool IsReporting => _activatedWithKey && _sendingEnabled;

        /// <summary>
        /// Activates AppMetrica with the project's API key once per process; safe to call repeatedly. Another
        /// SDK (ads, for one) may already have started AppMetrica anonymously; activating with the key
        /// upgrades that so the project's events reach its own application.
        /// </summary>
        public static void ActivateIfNeeded(
            string apiKey,
            bool crashReporting = true,
            bool nativeCrashReporting = true,
            bool firstActivationAsUpdate = false)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("AppMetrica API key must not be empty.", nameof(apiKey));

            if (_activatedWithKey)
                return;

            var config = new AppMetricaConfig(apiKey)
            {
                CrashReporting = crashReporting,
                NativeCrashReporting = nativeCrashReporting,
                FirstActivationAsUpdate = firstActivationAsUpdate,
            };

            Io.AppMetrica.AppMetrica.Activate(config);
            _activatedWithKey = true;
        }

        /// <summary>Whether the SDK is running at all in this process, possibly only anonymously for another SDK.</summary>
        public static bool IsActivated() => Io.AppMetrica.AppMetrica.IsActivated();

        /// <summary>Turns AppMetrica's own data sending on or off. Callers check <see cref="IsActivatedWithKey"/> first.</summary>
        public static void SetDataSendingEnabled(bool enabled)
        {
            _sendingEnabled = enabled;
            Io.AppMetrica.AppMetrica.SetDataSendingEnabled(enabled);
        }

        // The SDK is one per process, and so is this record of it. Entering Play Mode without a domain reload keeps statics
        // from the previous session: each session starts unactivated and its consent gate activates it again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetProcessState()
        {
            _activatedWithKey = false;
            _sendingEnabled = true;
        }
    }
}
