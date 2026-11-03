using System;
using System.Collections.Generic;
using Foundation.Logging;

namespace Foundation.Analytics
{
    /// <summary>
    /// Starts the analytics SDKs once the player has granted analytics consent, and applies later changes of that decision
    /// to the SDKs that are running. Revoking stops what the SDKs send on their own (sessions, crash reports), not only the
    /// events the game tracks itself: those are already dropped by the per-event consent checks in the backends.
    /// An SDK that throws is reported and skipped, so one failing SDK never keeps the others from starting or, on revoke,
    /// from stopping.
    /// </summary>
    public sealed class AnalyticsSdkGate
    {
        private readonly IReadOnlyList<IAnalyticsSdk> _sdks;
        private readonly Action<Exception> _reportFailure;
        private readonly IAppLogger _logger;

        public AnalyticsSdkGate(IEnumerable<IAnalyticsSdk> sdks, Action<Exception> reportFailure = null, IAppLogger logger = null)
        {
            if (sdks == null)
                throw new ArgumentNullException(nameof(sdks));

            _sdks = new List<IAnalyticsSdk>(sdks);
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            _reportFailure = reportFailure ?? (exception => _logger.Log(LogLevel.Error, "Analytics", exception.ToString()));
        }

        /// <summary>Starts every SDK that is not running yet, but only when consent is granted. Safe to call repeatedly.</summary>
        public void StartIfGranted(bool granted)
        {
            if (!granted)
                return;

            foreach (var sdk in _sdks)
                StartSdk(sdk);

            // Lifts an opt-out that an earlier decision may have left in an SDK.
            foreach (var sdk in _sdks)
                SetSending(sdk, true);
        }

        /// <summary>Applies a changed decision: granting starts what is not running; revoking stops sending in what is.</summary>
        public void Apply(bool granted)
        {
            if (granted)
            {
                StartIfGranted(true);
                return;
            }

            foreach (var sdk in _sdks)
                SetSending(sdk, false);
        }

        private void StartSdk(IAnalyticsSdk sdk)
        {
            try
            {
                if (!sdk.IsStarted)
                {
                    sdk.Start();
                    _logger.Log(LogLevel.Info, "Analytics", $"{sdk.GetType().Name} start: started={sdk.IsStarted}.");
                }
            }
            catch (Exception exception)
            {
                _reportFailure(exception);
            }
        }

        private void SetSending(IAnalyticsSdk sdk, bool enabled)
        {
            try
            {
                if (sdk.IsStarted)
                {
                    sdk.SetSendingEnabled(enabled);
                    _logger.Log(LogLevel.Info, "Analytics", $"{sdk.GetType().Name} sending enabled={enabled}.");
                }
            }
            catch (Exception exception)
            {
                _reportFailure(exception);
            }
        }
    }
}
