using System;
using System.Threading;
using Foundation.Logging;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;

namespace Foundation.Analytics
{
    public sealed class LoggingAnalyticsProvider : IAnalyticsProviderBackend
    {
        private readonly IAppLogger _logger;

        public LoggingAnalyticsProvider(IAppLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public UniTask TrackAsync(
            IAnalyticsEvent analyticsEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.Log(LogLevel.Info, "Analytics", analyticsEvent.Name);
            return UniTask.CompletedTask;
        }
    }
}
