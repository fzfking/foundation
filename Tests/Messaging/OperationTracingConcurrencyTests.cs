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
    public sealed class OperationTracingConcurrencyTests
    {
        [Test]
        public void InterleavedCommands_DoNotAdoptEachOthersOperationsAsChildren()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            var gateA = new UniTaskCompletionSource();
            var gateB = new UniTaskCompletionSource();
            var commands = new CommandBus();
            commands.RegisterHandler(new GatedHandler<CommandA>(gateA, events, () => new EventA()));
            commands.RegisterHandler(new GatedHandler<CommandB>(gateB, events, () => new EventB()));
            var bus = new TracingCommandBus(commands, tracer);

            var taskA = bus.SendAsync(new CommandA());
            var taskB = bus.SendAsync(new CommandB());
            gateA.TrySetResult();
            gateB.TrySetResult();
            taskA.GetAwaiter().GetResult();
            taskB.GetAwaiter().GetResult();

            var commandA = log.Single("Command CommandA");
            var commandB = log.Single("Command CommandB");
            var eventA = log.Single("Event EventA");
            var eventB = log.Single("Event EventB");

            Assert.That(commandA.CorrelationId, Is.Not.EqualTo(commandB.CorrelationId),
                "two independently started commands are separate root operations");
            Assert.That(eventA.CorrelationId, Is.Not.EqualTo(commandB.CorrelationId),
                "an event published by command A must never be attributed to command B");
            Assert.That(eventB.CorrelationId, Is.Not.EqualTo(commandA.CorrelationId),
                "an event published by command B must never be attributed to command A");
            Assert.That(eventA.CorrelationId, Is.Not.EqualTo(eventB.CorrelationId));
        }

        [Test]
        public void OperationStartedAfterASuspension_BecomesItsOwnRootInsteadOfAFalseChild()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            var gate = new UniTaskCompletionSource();
            var commands = new CommandBus();
            commands.RegisterHandler(new GatedHandler<CommandA>(gate, events, () => new EventA()));

            var task = new TracingCommandBus(commands, tracer).SendAsync(new CommandA());

            // While command A is parked, an unrelated flow starts. It must not be nested under it.
            events.PublishAsync(new EventB()).GetAwaiter().GetResult();
            gate.TrySetResult();
            task.GetAwaiter().GetResult();

            var commandA = log.Single("Command CommandA");
            var unrelated = log.Single("Event EventB");

            Assert.That(unrelated.Depth, Is.Zero);
            Assert.That(unrelated.CorrelationId, Is.Not.EqualTo(commandA.CorrelationId));
        }

        [Test]
        public void SynchronousFlow_StillProducesTheCompleteNestedTree()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var events = new TracingEventBus(new EventBus(), tracer);
            var commands = new CommandBus();
            commands.RegisterHandler(new SynchronousHandler(events));

            new TracingCommandBus(commands, tracer).SendAsync(new CommandA()).GetAwaiter().GetResult();

            var commandA = log.Single("Command CommandA");
            var eventA = log.Single("Event EventA");

            Assert.That(eventA.CorrelationId, Is.EqualTo(commandA.CorrelationId));
            Assert.That(eventA.Depth, Is.EqualTo(1));
        }

        [Test]
        public void SuspendedCommand_StillReportsItsOwnCompletionOnTheSameCorrelation()
        {
            var log = new RecordingLogger();
            var tracer = new LoggingOperationTracer(log);
            var gate = new UniTaskCompletionSource();
            var commands = new CommandBus();
            commands.RegisterHandler(new GatedHandler<CommandA>(gate, null, () => new EventA()));

            var task = new TracingCommandBus(commands, tracer).SendAsync(new CommandA());
            gate.TrySetResult();
            task.GetAwaiter().GetResult();

            var start = log.Entries.Single(entry => entry.Message.StartsWith("> Command CommandA", StringComparison.Ordinal));
            var end = log.Entries.Single(entry => entry.Message.StartsWith("< Command CommandA", StringComparison.Ordinal));

            Assert.That(end.CorrelationId, Is.EqualTo(start.CorrelationId));
            Assert.That(end.Depth, Is.EqualTo(start.Depth));
        }

        private sealed class CommandA
        {
        }

        private sealed class CommandB
        {
        }

        private sealed class EventA
        {
        }

        private sealed class EventB
        {
        }

        private sealed class GatedHandler<TCommand> : ICommandHandler<TCommand>
        {
            private readonly UniTaskCompletionSource _gate;
            private readonly IEventBus _events;
            private readonly Func<object> _factory;

            public GatedHandler(UniTaskCompletionSource gate, IEventBus events, Func<object> factory)
            {
                _gate = gate;
                _events = events;
                _factory = factory;
            }

            public async UniTask HandleAsync(TCommand command, CancellationToken cancellationToken = default)
            {
                await _gate.Task;
                if (_events == null)
                    return;

                var message = _factory();
                if (message is EventA eventA)
                    await _events.PublishAsync(eventA, cancellationToken);
                else
                    await _events.PublishAsync((EventB)message, cancellationToken);
            }
        }

        private sealed class SynchronousHandler : ICommandHandler<CommandA>
        {
            private readonly IEventBus _events;

            public SynchronousHandler(IEventBus events) => _events = events;

            public UniTask HandleAsync(CommandA command, CancellationToken cancellationToken = default)
                => _events.PublishAsync(new EventA(), cancellationToken);
        }

        private sealed class RecordingLogger : IAppLogger
        {
            public List<TraceEntry> Entries { get; } = new();

            public void Log(LogLevel level, string category, string message)
            {
                var correlationEnd = message.IndexOf("] ", StringComparison.Ordinal);
                var correlationId = message.Substring(1, correlationEnd - 1);
                var body = message.Substring(correlationEnd + 2);
                var depth = 0;
                while (body.StartsWith("  ", StringComparison.Ordinal))
                {
                    depth++;
                    body = body.Substring(2);
                }

                Entries.Add(new TraceEntry(correlationId, depth, body));
            }

            public TraceEntry Single(string operation)
                => Entries.Single(entry => entry.Message.StartsWith("> " + operation, StringComparison.Ordinal));
        }

        private sealed class TraceEntry
        {
            public TraceEntry(string correlationId, int depth, string message)
            {
                CorrelationId = correlationId;
                Depth = depth;
                Message = message;
            }

            public string CorrelationId { get; }
            public int Depth { get; }
            public string Message { get; }
        }
    }
}
