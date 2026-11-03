using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foundation.Diagnostics;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Messaging.Tracing;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Messaging
{
    public sealed class OperationTracingTests
    {
        [Test]
        public void CommandPublishingAnEvent_ProducesOneNestedCausalityTree()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            var commands = new CommandBus();
            commands.RegisterHandler(new PlaceHandler(events));
            var bus = new TracingCommandBus(commands, tracer);
            events.Subscribe(new AchievementSubscriber(events));

            bus.SendAsync(new PlaceCommand()).GetAwaiter().GetResult();

            Assert.That(log.Operations, Is.EqualTo(new[]
            {
                "> Command PlaceCommand",
                "  > Event BuildingPlacedFact",
                "    > Subscriber AchievementSubscriber",
                "      > Event AchievementCompletedFact",
                "      < Event AchievementCompletedFact",
                "    < Subscriber AchievementSubscriber",
                "  < Event BuildingPlacedFact",
                "< Command PlaceCommand"
            }));
        }

        [Test]
        public void EveryOperationInOneFlow_SharesTheRootCorrelationId()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            var commands = new CommandBus();
            commands.RegisterHandler(new PlaceHandler(events));
            events.Subscribe(new AchievementSubscriber(events));

            new TracingCommandBus(commands, tracer).SendAsync(new PlaceCommand()).GetAwaiter().GetResult();

            Assert.That(log.CorrelationIds.Distinct().ToArray(), Is.EqualTo(new[] { "1" }));
        }

        [Test]
        public void SeparateFlows_GetSeparateCorrelationIds()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);

            events.PublishAsync(new BuildingPlacedFact()).GetAwaiter().GetResult();
            events.PublishAsync(new BuildingPlacedFact()).GetAwaiter().GetResult();

            Assert.That(log.CorrelationIds.Distinct().ToArray(), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void SubscriberException_IsTracedAndStillPropagates()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            events.Subscribe(new ThrowingSubscriber());

            Assert.Throws<InvalidOperationException>(
                () => events.PublishAsync(new BuildingPlacedFact()).GetAwaiter().GetResult());

            Assert.That(log.Messages.Any(message => message.Contains("! Subscriber ThrowingSubscriber")), Is.True);
            Assert.That(log.Messages.Any(message => message.Contains("boom")), Is.True);
            Assert.That(log.Messages.Any(message => message.Contains("! Event BuildingPlacedFact")), Is.True,
                "the failure must be visible on the publishing operation too");
        }

        [Test]
        public void CompletedOperation_ReportsItsDuration()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);

            new TracingEventBus(new EventBus(), tracer)
                .PublishAsync(new BuildingPlacedFact())
                .GetAwaiter()
                .GetResult();

            Assert.That(log.Messages.Any(message => message.Contains("< Event BuildingPlacedFact") && message.Contains("ms")), Is.True);
        }

        [Test]
        public void DisabledCategory_ProducesNoOutput()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(
                log,
                new OperationTraceOptions(OperationTraceCategory.Command));
            var events = new TracingEventBus(new EventBus(), tracer);
            events.Subscribe(new AchievementSubscriber(null));

            events.PublishAsync(new BuildingPlacedFact()).GetAwaiter().GetResult();

            Assert.That(log.Messages, Is.Empty);
        }

        [Test]
        public void SimulationCategory_IsOffByDefault()
        {
            var tracer = new LoggingOperationTracer(new RecordingLogger());

            Assert.That(tracer.IsEnabled(OperationTraceCategory.Simulation), Is.False);
            Assert.That(tracer.IsEnabled(OperationTraceCategory.Command), Is.True);
        }

        [Test]
        public void MutedName_IsFilteredOutWhileTheRestKeepsTracing()
        {
            var log = new RecordingLogger();
            var options = new OperationTraceOptions().Mute(nameof(BuildingPlacedFact));
            var events = new TracingEventBus(new EventBus(), new LoggingOperationTracer(log, options));

            events.PublishAsync(new BuildingPlacedFact()).GetAwaiter().GetResult();
            events.PublishAsync(new AchievementCompletedFact()).GetAwaiter().GetResult();

            Assert.That(log.Operations, Is.EqualTo(new[]
            {
                "> Event AchievementCompletedFact",
                "< Event AchievementCompletedFact"
            }));
        }

        [Test]
        public void NullTracer_DoesNotAllocateScopesOrLog()
        {
            var tracer = NullOperationTracer.Instance;

            Assert.That(tracer.IsEnabled(OperationTraceCategory.Command), Is.False);
            using var scope = tracer.Begin(OperationTraceCategory.Command, "Command", "Anything");
            Assert.That(scope.CorrelationId, Is.Null);
        }

        [Test]
        public void TracingBuses_ForwardMessagesUnchanged()
        {
            var tracer = new LoggingOperationTracer(new RecordingLogger());
            var queries = new QueryBus();
            queries.RegisterHandler(new CountHandler());

            var result = new TracingQueryBus(queries, tracer)
                .QueryAsync<CountQuery, int>(new CountQuery(7))
                .GetAwaiter()
                .GetResult();

            Assert.That(result, Is.EqualTo(7));
        }

        private sealed class PlaceCommand
        {
        }

        private sealed class BuildingPlacedFact
        {
        }

        private sealed class AchievementCompletedFact
        {
        }

        private sealed class CountQuery
        {
            public CountQuery(int value) => Value = value;

            public int Value { get; }
        }

        private sealed class CountHandler : IQueryHandler<CountQuery, int>
        {
            public UniTask<int> HandleAsync(CountQuery query, CancellationToken cancellationToken = default)
                => UniTask.FromResult(query.Value);
        }

        private sealed class PlaceHandler : ICommandHandler<PlaceCommand>
        {
            private readonly IEventBus _events;

            public PlaceHandler(IEventBus events) => _events = events;

            public UniTask HandleAsync(PlaceCommand command, CancellationToken cancellationToken = default)
                => _events.PublishAsync(new BuildingPlacedFact(), cancellationToken);
        }

        private sealed class AchievementSubscriber : IEventHandler<BuildingPlacedFact>
        {
            private readonly IEventBus _events;

            public AchievementSubscriber(IEventBus events) => _events = events;

            public UniTask HandleAsync(BuildingPlacedFact message, CancellationToken cancellationToken = default)
                => _events == null
                    ? UniTask.CompletedTask
                    : _events.PublishAsync(new AchievementCompletedFact(), cancellationToken);
        }

        private sealed class ThrowingSubscriber : IEventHandler<BuildingPlacedFact>
        {
            public UniTask HandleAsync(BuildingPlacedFact message, CancellationToken cancellationToken = default)
                => throw new InvalidOperationException("boom");
        }

        private sealed class RecordingLogger : IAppLogger
        {
            public List<string> Messages { get; } = new();

            public List<string> CorrelationIds { get; } = new();

            public IReadOnlyList<string> Operations => Messages
                .Select(StripCorrelation)
                .Select(StripDuration)
                .ToArray();

            public void Log(LogLevel level, string category, string message)
            {
                Messages.Add(message);
                var end = message.IndexOf(']');
                if (message.StartsWith("[", StringComparison.Ordinal) && end > 0)
                    CorrelationIds.Add(message.Substring(1, end - 1));
            }

            private static string StripCorrelation(string message)
            {
                var end = message.IndexOf("] ", StringComparison.Ordinal);
                return end < 0 ? message : message.Substring(end + 2);
            }

            private static string StripDuration(string message)
            {
                var open = message.LastIndexOf(" (", StringComparison.Ordinal);
                return open < 0 ? message : message.Substring(0, open);
            }
        }
    }
}
