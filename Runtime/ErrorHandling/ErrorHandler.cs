using System;
using Foundation.Logging;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;

namespace Foundation.ErrorHandling
{
    public interface IErrorHandler
    {
        void Report(ErrorContext context);
    }

    public sealed class ErrorHandler : IErrorHandler
    {
        private readonly IAppLogger _logger;
        private readonly IEventBus _eventBus;

        public ErrorHandler(IAppLogger logger, IEventBus eventBus)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        }

        public void Report(ErrorContext context)
        {
            if (context == null)
                throw new ArgumentNullException(nameof(context));
            _logger.Log(ToLogLevel(context.Severity), "ErrorHandling", context.Message);
            _eventBus.PublishAsync(new ErrorReportedEvent(context)).Forget();
        }

        private static LogLevel ToLogLevel(ErrorSeverity severity)
            => severity == ErrorSeverity.Warning ? LogLevel.Warning : LogLevel.Error;
    }
}
