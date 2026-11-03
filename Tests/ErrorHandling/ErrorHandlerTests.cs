using System;
using System.Threading;
using Foundation.ErrorHandling;
using Foundation.Logging;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.ErrorHandling
{
    public sealed class ErrorHandlerTests
    {
        [TestCase(ErrorSeverity.Warning, LogLevel.Warning)]
        [TestCase(ErrorSeverity.Error, LogLevel.Error)]
        public void Report_LogsAtTheMatchingLevel_AndPublishesErrorEvent(ErrorSeverity severity, LogLevel expected)
        {
            var logger = new RecordingLogger();
            var bus = new RecordingEventBus();
            var handler = new ErrorHandler(logger, bus);
            var context = new ErrorContext("test.error", "Something failed", severity);

            handler.Report(context);

            Assert.That(logger.Level, Is.EqualTo(expected));
            Assert.That(logger.Message, Is.EqualTo("Something failed"));
            Assert.That(bus.Event, Is.TypeOf<ErrorReportedEvent>());
            Assert.That(((ErrorReportedEvent)bus.Event).Context, Is.SameAs(context));
        }

        [Test]
        public void MissingCode_FailsClearly()
        {
            Assert.Throws<ArgumentException>(() => new ErrorContext("", "message"));
        }

        private sealed class RecordingLogger : IAppLogger
        {
            public LogLevel Level;
            public string Message;
            public void Log(LogLevel level, string category, string message) { Level = level; Message = message; }
        }

        private sealed class RecordingEventBus : IEventBus
        {
            public object Event;
            public IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler) => throw new NotSupportedException();
            public UniTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default) { Event = message; return UniTask.CompletedTask; }
        }
    }
}
