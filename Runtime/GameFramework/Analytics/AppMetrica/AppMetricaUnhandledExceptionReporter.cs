using System;
using Foundation.Lifecycle;
using UnityEngine;

namespace Foundation.GameFramework.Analytics.AppMetrica
{
    /// <summary>
    /// Native crash reporting never sees a managed exception that Unity caught and only logged (an unhandled
    /// exception in a UniTask continuation, <c>Debug.LogException</c>). This forwards exactly those to
    /// <c>AppMetrica.ReportUnhandledException</c>.
    /// Uses <see cref="Application.logMessageReceivedThreaded"/>: UniTask and background work log off the
    /// main thread, and those must not be dropped.
    /// </summary>
    public sealed class AppMetricaUnhandledExceptionReporter : IStartableEntryPoint, IDisposable
    {
        private bool _subscribed;

        public void Start()
        {
            if (_subscribed)
                return;

            Application.logMessageReceivedThreaded += OnLogMessageReceived;
            _subscribed = true;
        }

        public void Dispose()
        {
            if (!_subscribed)
                return;

            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
            _subscribed = false;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception)
                return;

            // Not the SDK's own activation: another SDK may run it anonymously before the player has consented.
            if (!AppMetricaActivation.IsReporting)
                return;

            // Unity's log callback gives text, not the Exception. AppMetrica groups by stack trace, so a
            // reconstructed exception carrying Unity's stackTrace string groups sensibly instead of every
            // report being its own group.
            var reconstructed = new UnityLoggedException(condition, stackTrace);
            Io.AppMetrica.AppMetrica.ReportUnhandledException(reconstructed);
        }

        private sealed class UnityLoggedException : Exception
        {
            private readonly string _capturedStackTrace;

            public override string StackTrace => _capturedStackTrace;

            public UnityLoggedException(string condition, string stackTrace) : base(condition)
            {
                _capturedStackTrace = stackTrace;
            }
        }
    }
}
