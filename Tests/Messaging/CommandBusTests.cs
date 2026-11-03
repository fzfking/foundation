using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.DI;
using Foundation.Messaging;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Messaging
{
    public sealed class CommandBusTests
    {
        private class TestCommand { }

        private sealed class DerivedCommand : TestCommand { }

        [Test]
        public void Send_InvokesRegisteredHandler()
        {
            var handler = new RecordingHandler();
            var bus = new CommandBus();
            bus.RegisterHandler<TestCommand>(handler);

            bus.SendAsync(new TestCommand()).GetAwaiter().GetResult();

            Assert.That(handler.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void SendWithoutHandler_FailsClearly()
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await new CommandBus().SendAsync(new TestCommand()));
        }

        [Test]
        public void DuplicateHandlerRegistration_FailsClearly()
        {
            var bus = new CommandBus();
            bus.RegisterHandler<TestCommand>(new RecordingHandler());

            Assert.Throws<InvalidOperationException>(() => bus.RegisterHandler<TestCommand>(new RecordingHandler()));
        }

        // Regression: a bus once checked the handler by the sent type but dispatched by the runtime type, so a
        // derived command sent as its base type passed the check and was then dropped without an error.
        [Test]
        public void ADerivedCommandSentAsItsBaseType_ReachesTheBaseHandler()
        {
            var handler = new RecordingHandler();
            var bus = new CommandBus();
            bus.RegisterHandler<TestCommand>(handler);

            bus.SendAsync<TestCommand>(new DerivedCommand()).GetAwaiter().GetResult();

            Assert.That(handler.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void ADisposedRegistration_FreesTheCommand_AndNeverRemovesTheHandlerThatReplacedIt()
        {
            var bus = new CommandBus();
            var first = bus.RegisterHandler<TestCommand>(new RecordingHandler());
            first.Dispose();
            Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(new TestCommand()));

            var replacement = new RecordingHandler();
            bus.RegisterHandler<TestCommand>(replacement);
            first.Dispose();
            bus.SendAsync(new TestCommand()).GetAwaiter().GetResult();

            Assert.That(replacement.CallCount, Is.EqualTo(1));
        }

        [Test]
        public void SerializedSend_CancellationRacingWithExitDoesNotCorruptTheGate()
        {
            var bus = new CommandBus(new MessagingOptions(commands: CommandExecutionMode.Serialized));
            var handler = new BlockingFirstHandler();
            bus.RegisterHandler<IndexedCommand>(handler);
            using var cancellation = new CancellationTokenSource();

            var first = bus.SendAsync(new IndexedCommand(1));
            handler.FirstStarted.Task.GetAwaiter().GetResult();
            var second = bus.SendAsync(new IndexedCommand(2), cancellation.Token);
            using var race = new Barrier(3);
            var cancel = new Thread(() =>
            {
                race.SignalAndWait();
                cancellation.Cancel();
            });
            var release = new Thread(() =>
            {
                race.SignalAndWait();
                handler.ReleaseFirst.TrySetResult();
            });
            cancel.Start();
            release.Start();
            race.SignalAndWait();
            cancel.Join();
            release.Join();

            first.GetAwaiter().GetResult();
            var canceled = false;
            try
            {
                second.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            bus.SendAsync(new IndexedCommand(3)).GetAwaiter().GetResult();

            Assert.That(handler.Calls[0], Is.EqualTo(1));
            Assert.That(handler.Calls[handler.Calls.Count - 1], Is.EqualTo(3));
            Assert.That(handler.Calls.Contains(2), Is.EqualTo(!canceled));
        }

        // Regression: context handlers registered into the application bus stayed there after the context was disposed,
        // and entering the context again failed on the duplicate registration.
        [Test]
        public void AContextScopeHandler_LeavesWithTheScope_AndTheContextCanBeEnteredAgain()
        {
            using var application = MinimalServiceScope.Create(registry => registry.RegisterMessaging());
            var bus = application.Resolve<ICommandBus>();

            RecordingHandler first;
            using (var context = application.CreateScope(registry => registry.RegisterCommandHandler<TestCommand, RecordingHandler>()))
            {
                first = context.Resolve<RecordingHandler>();
                bus.SendAsync(new TestCommand()).GetAwaiter().GetResult();
            }

            Assert.ThrowsAsync<InvalidOperationException>(async () => await bus.SendAsync(new TestCommand()),
                "a disposed context's handler must not be reachable");

            using var reentered = application.CreateScope(registry => registry.RegisterCommandHandler<TestCommand, RecordingHandler>());
            bus.SendAsync(new TestCommand()).GetAwaiter().GetResult();

            Assert.That(first.CallCount, Is.EqualTo(1));
            Assert.That(reentered.Resolve<RecordingHandler>().CallCount, Is.EqualTo(1));
        }

        private sealed class RecordingHandler : ICommandHandler<TestCommand>
        {
            public int CallCount { get; private set; }

            public UniTask HandleAsync(TestCommand command, CancellationToken cancellationToken = default)
            {
                CallCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class IndexedCommand
        {
            public int Index { get; }

            public IndexedCommand(int index) => Index = index;
        }

        private sealed class BlockingFirstHandler : ICommandHandler<IndexedCommand>
        {
            private readonly object _sync = new();

            public readonly UniTaskCompletionSource FirstStarted = new();
            public readonly UniTaskCompletionSource ReleaseFirst = new();
            public readonly List<int> Calls = new();

            public async UniTask HandleAsync(IndexedCommand command, CancellationToken cancellationToken = default)
            {
                lock (_sync)
                    Calls.Add(command.Index);
                if (command.Index != 1)
                    return;

                FirstStarted.TrySetResult();
                await ReleaseFirst.Task;
            }
        }
    }
}
