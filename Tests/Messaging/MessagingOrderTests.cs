using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.Messaging;
using NUnit.Framework;

namespace Foundation.Tests.Messaging
{
    public sealed class MessagingOrderTests
    {
        private sealed class Ping { }

        private sealed class Step
        {
            public readonly string Name;

            public Step(string name)
            {
                Name = name;
            }
        }

        [Test]
        public void SequentialEvents_StartTheNextHandlerOnlyAfterThePreviousFinished()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            using var first = bus.Subscribe(new EventHandler<Ping>(async _ => { log.Add("first:start"); await release.Task; log.Add("first:end"); }));
            using var second = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("second"); return UniTask.CompletedTask; }));

            var publish = bus.PublishAsync(new Ping());

            Assert.That(log, Is.EqualTo(new[] { "first:start" }), "the second handler waits for the first");
            release.TrySetResult();
            publish.GetAwaiter().GetResult();
            Assert.That(log, Is.EqualTo(new[] { "first:start", "first:end", "second" }));
        }

        [Test]
        public void ConcurrentEvents_StartEveryHandlerBeforeAnyFinishes()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Concurrent));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            using var first = bus.Subscribe(new EventHandler<Ping>(async _ => { log.Add("first:start"); await release.Task; }));
            using var second = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("second"); return UniTask.CompletedTask; }));

            var publish = bus.PublishAsync(new Ping());

            Assert.That(log, Is.EqualTo(new[] { "first:start", "second" }));
            release.TrySetResult();
            publish.GetAwaiter().GetResult();
        }

        [Test]
        public void SequentialEvents_ALaterHandlerStillGetsTheEvent_WhenAnEarlierOneFails()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var log = new List<string>();
            using var failing = bus.Subscribe(new EventHandler<Ping>(_ => throw new InvalidOperationException("boom")));
            using var listening = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("listening"); return UniTask.CompletedTask; }));

            Assert.Throws<InvalidOperationException>(() => bus.PublishAsync(new Ping()).GetAwaiter().GetResult());

            Assert.That(log, Is.EqualTo(new[] { "listening" }));
        }

        [Test]
        public void SequentialEvents_ACancelledPublish_DoesNotStartTheHandlersThatHaveNotRunYet()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var log = new List<string>();
            using var cancellation = new CancellationTokenSource();
            using var first = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("first"); cancellation.Cancel(); return UniTask.CompletedTask; }));
            using var second = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("second"); return UniTask.CompletedTask; }));

            Assert.Throws<OperationCanceledException>(() => bus.PublishAsync(new Ping(), cancellation.Token).GetAwaiter().GetResult());

            Assert.That(log, Is.EqualTo(new[] { "first" }));
        }

        [Test]
        public void SequentialEvents_AHandlerThatUnsubscribedWhileAnEarlierOneAwaited_IsNotCalled()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            using var first = bus.Subscribe(new EventHandler<Ping>(async _ => { log.Add("first"); await release.Task; }));
            var closedView = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("closed view"); return UniTask.CompletedTask; }));
            using var last = bus.Subscribe(new EventHandler<Ping>(_ => { log.Add("last"); return UniTask.CompletedTask; }));

            var publish = bus.PublishAsync(new Ping());
            closedView.Dispose();
            release.TrySetResult();
            publish.GetAwaiter().GetResult();

            Assert.That(log, Is.EqualTo(new[] { "first", "last" }));
        }

        [Test]
        public void SequentialEvents_ADisposedSubscriptionOfAHandlerSubscribedTwice_DoesNotStopTheOtherOne()
        {
            var bus = new EventBus(new MessagingOptions(events: EventDispatchMode.Sequential));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            var handler = new EventHandler<Ping>(_ => { log.Add("handler"); return UniTask.CompletedTask; });
            using var waiting = bus.Subscribe(new EventHandler<Ping>(async _ => { log.Add("waiting"); await release.Task; }));
            var firstSubscription = bus.Subscribe(handler);
            using var secondSubscription = bus.Subscribe(handler);

            var publish = bus.PublishAsync(new Ping());
            firstSubscription.Dispose();
            release.TrySetResult();
            publish.GetAwaiter().GetResult();

            Assert.That(log, Is.EqualTo(new[] { "waiting", "handler" }), "the handler keeps one subscription, so it runs once");
        }

        [Test]
        public void ImmediateCommands_MayOverlap()
        {
            var bus = new CommandBus(new MessagingOptions(commands: CommandExecutionMode.Immediate));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            bus.RegisterHandler<Step>(new CommandHandler<Step>(async step =>
            {
                log.Add(step.Name + ":start");
                if (step.Name == "a")
                    await release.Task;
                log.Add(step.Name + ":end");
            }));

            var a = bus.SendAsync(new Step("a"));
            var b = bus.SendAsync(new Step("b"));

            Assert.That(log, Is.EqualTo(new[] { "a:start", "b:start", "b:end" }));
            release.TrySetResult();
            a.GetAwaiter().GetResult();
            b.GetAwaiter().GetResult();
        }

        [Test]
        public void SerializedCommands_RunOneAtATimeInTheOrderTheyWereSent()
        {
            var bus = new CommandBus(new MessagingOptions(commands: CommandExecutionMode.Serialized));
            var log = new List<string>();
            var gates = new Dictionary<string, UniTaskCompletionSource>
            {
                ["a"] = new UniTaskCompletionSource(),
                ["b"] = new UniTaskCompletionSource(),
            };
            bus.RegisterHandler<Step>(new CommandHandler<Step>(async step =>
            {
                log.Add(step.Name + ":start");
                if (gates.TryGetValue(step.Name, out var gate))
                    await gate.Task;
                log.Add(step.Name + ":end");
            }));

            var a = bus.SendAsync(new Step("a"));
            var b = bus.SendAsync(new Step("b"));
            var c = bus.SendAsync(new Step("c"));
            Assert.That(log, Is.EqualTo(new[] { "a:start" }), "b and c wait for a");

            gates["a"].TrySetResult();
            Assert.That(log, Is.EqualTo(new[] { "a:start", "a:end", "b:start" }), "b gets the turn before c");

            gates["b"].TrySetResult();
            a.GetAwaiter().GetResult();
            b.GetAwaiter().GetResult();
            c.GetAwaiter().GetResult();
            Assert.That(log, Is.EqualTo(new[] { "a:start", "a:end", "b:start", "b:end", "c:start", "c:end" }));
        }

        // A failed command must hand the turn on, or one bad command would freeze every later one.
        [Test]
        public void SerializedCommands_ACommandThatFailsDoesNotBlockTheNext()
        {
            var bus = new CommandBus(new MessagingOptions(commands: CommandExecutionMode.Serialized));
            var ran = new List<string>();
            bus.RegisterHandler<Step>(new CommandHandler<Step>(step =>
            {
                if (step.Name == "bad")
                    throw new InvalidOperationException("boom");
                ran.Add(step.Name);
                return UniTask.CompletedTask;
            }));

            Assert.Throws<InvalidOperationException>(() => bus.SendAsync(new Step("bad")).GetAwaiter().GetResult());
            bus.SendAsync(new Step("good")).GetAwaiter().GetResult();

            Assert.That(ran, Is.EqualTo(new[] { "good" }));
        }

        [Test]
        public void SerializedCommands_AWaitingCommandThatIsCancelledNeverRuns_AndTheQueueMovesOn()
        {
            var bus = new CommandBus(new MessagingOptions(commands: CommandExecutionMode.Serialized));
            var log = new List<string>();
            var release = new UniTaskCompletionSource();
            bus.RegisterHandler<Step>(new CommandHandler<Step>(async step =>
            {
                log.Add(step.Name);
                if (step.Name == "a")
                    await release.Task;
            }));
            using var cancellation = new CancellationTokenSource();

            var a = bus.SendAsync(new Step("a"));
            var cancelled = bus.SendAsync(new Step("cancelled"), cancellation.Token);
            var c = bus.SendAsync(new Step("c"));
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => cancelled.GetAwaiter().GetResult());
            release.TrySetResult();
            a.GetAwaiter().GetResult();
            c.GetAwaiter().GetResult();
            Assert.That(log, Is.EqualTo(new[] { "a", "c" }));
        }

        // The options are chosen where the buses are registered; losing them there would leave the game on the defaults.
        [Test]
        public void RegisterMessaging_HandsItsOptionsToTheBuses()
        {
            using var application = MinimalServiceScope.Create(registry => registry.RegisterMessaging(
                new MessagingOptions(EventDispatchMode.Concurrent, CommandExecutionMode.Serialized)));
            var events = application.Resolve<IEventBus>();
            var commands = application.Resolve<ICommandBus>();
            var release = new UniTaskCompletionSource();
            var log = new List<string>();
            using var first = events.Subscribe(new EventHandler<Ping>(async _ => { log.Add("e1"); await release.Task; }));
            using var second = events.Subscribe(new EventHandler<Ping>(_ => { log.Add("e2"); return UniTask.CompletedTask; }));
            ((ICommandHandlerRegistry)commands).RegisterHandler<Step>(new CommandHandler<Step>(async step =>
            {
                log.Add(step.Name);
                if (step.Name == "c1")
                    await release.Task;
            }));

            var publish = events.PublishAsync(new Ping());
            var c1 = commands.SendAsync(new Step("c1"));
            var c2 = commands.SendAsync(new Step("c2"));

            Assert.That(log, Is.EqualTo(new[] { "e1", "e2", "c1" }), "events concurrent, second command held back");
            release.TrySetResult();
            publish.GetAwaiter().GetResult();
            c1.GetAwaiter().GetResult();
            c2.GetAwaiter().GetResult();
            Assert.That(log[log.Count - 1], Is.EqualTo("c2"));
        }

        private sealed class EventHandler<TEvent> : IEventHandler<TEvent>
        {
            private readonly Func<TEvent, UniTask> _handle;

            public EventHandler(Func<TEvent, UniTask> handle)
            {
                _handle = handle;
            }

            public UniTask HandleAsync(TEvent message, CancellationToken cancellationToken = default) => _handle(message);
        }

        private sealed class CommandHandler<TCommand> : ICommandHandler<TCommand>
        {
            private readonly Func<TCommand, UniTask> _handle;

            public CommandHandler(Func<TCommand, UniTask> handle)
            {
                _handle = handle;
            }

            public UniTask HandleAsync(TCommand command, CancellationToken cancellationToken = default) => _handle(command);
        }
    }
}
