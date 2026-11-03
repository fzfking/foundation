using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Sequences;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.Sequences
{
    public sealed class SequenceRunnerTests
    {
        private List<string> _log;
        private SequenceHandlerRegistry _handlers;
        private SequenceRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _log = new();
            _handlers = new();
            _handlers.RegisterAction("record", (context, _) =>
            {
                _log.Add(context.Arguments[0]);
                return UniTask.CompletedTask;
            });
            _handlers.RegisterCondition("equals", context => context.Arguments[0] == "yes");
            _runner = new(_handlers);
        }

        [TestCase(false, "yes", "then")]
        [TestCase(false, "no", "else")]
        [TestCase(true, "no", "then")]
        public void Conditional_ExecutesOnlySelectedBranch(bool negated, string value, string expected)
        {
            var node = new SequenceNode
            {
                Kind = SequenceNodeKind.If, Command = "equals", Arguments = new[] { value }, Negated = negated,
                Children = new[] { SequenceNode.Action("record", "then") },
                ElseChildren = new[] { SequenceNode.Action("record", "else") },
            };
            Run(new[] { node }).GetAwaiter().GetResult();
            Assert.That(_log, Is.EqualTo(new[] { expected }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Skip_ExitsNormallyAndRunsCleanup(bool negated)
        {
            var args = negated ? new[] { "not", "equals", "no" } : new[] { "equals", "yes" };
            Run(new[] { new SequenceNode { Command = "skip_if", Arguments = args }, SequenceNode.Action("record", "unreachable") },
                new[] { SequenceNode.Action("record", "cleanup") }).GetAwaiter().GetResult();
            Assert.That(_log, Is.EqualTo(new[] { "cleanup" }));
        }

        [Test]
        public void EventWait_BlocksFollowingActionUntilSignal_AndReceivesContext()
        {
            var signal = new UniTaskCompletionSource();
            _handlers.RegisterWait("signal", (context, _) =>
            {
                Assert.That(context.SequenceId, Is.EqualTo("sequence"));
                Assert.That(context.StepId, Is.EqualTo("step"));
                Assert.That(context.StepIndex, Is.EqualTo(3));
                Assert.That(context.Line, Is.EqualTo(7));
                Assert.That(context.Arguments, Is.EqualTo(new[] { "event-id" }));
                return signal.Task;
            });
            var task = Run(new[] { new SequenceNode { Command = "wait", Arguments = new[] { "signal", "event-id" }, Line = 7 },
                SequenceNode.Action("record", "after") });
            Assert.That(task.IsCompleted, Is.False);
            Assert.That(_log, Is.Empty);
            signal.TrySetResult();
            task.GetAwaiter().GetResult();
            Assert.That(_log, Is.EqualTo(new[] { "after" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Cancellation_StopsExecutionAndCleansUpWithUncancelledToken(bool beforeStart)
        {
            var signal = new UniTaskCompletionSource();
            _handlers.RegisterWait("signal", async (_, token) =>
            {
                using var registration = token.Register(() => signal.TrySetCanceled(token));
                await signal.Task;
            });
            _handlers.RegisterAction("cleanup", (_, token) =>
            {
                Assert.That(token.IsCancellationRequested, Is.False);
                _log.Add("cleanup");
                return UniTask.CompletedTask;
            });
            using var cancellation = new CancellationTokenSource();
            if (beforeStart)
                cancellation.Cancel();
            var task = Run(new[] { new SequenceNode { Command = "wait", Arguments = new[] { "signal" } },
                SequenceNode.Action("record", "unreachable") }, new[] { SequenceNode.Action("cleanup") }, cancellation.Token);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.That(_log, Is.EqualTo(new[] { "cleanup" }));
        }

        [Test]
        public void Failure_PropagatesAndRunsCleanup()
        {
            var task = Run(new[] { SequenceNode.Action("missing") }, new[] { SequenceNode.Action("record", "cleanup") });
            Assert.Throws<InvalidOperationException>(() => task.GetAwaiter().GetResult());
            Assert.That(_log, Is.EqualTo(new[] { "cleanup" }));
        }

        [Test]
        public void CleanupFailure_DoesNotMaskBodyFailure()
        {
            _handlers.RegisterAction("fail", (context, _) => throw new InvalidOperationException(context.Arguments[0]));

            var failure = Assert.Throws<AggregateException>(() => Run(
                new[] { SequenceNode.Action("fail", "body") },
                new[] { SequenceNode.Action("fail", "cleanup") }).GetAwaiter().GetResult());

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0].Message, Is.EqualTo("body"));
            Assert.That(failure.InnerExceptions[1].Message, Is.EqualTo("cleanup"));
        }

        [Test]
        public void Registry_RejectsDuplicateHandlers_ButAllowsSameKeyInDifferentCategories()
        {
            Assert.Throws<InvalidOperationException>(() => _handlers.RegisterCondition("equals", _ => false));
            Assert.DoesNotThrow(() => _handlers.RegisterWait("equals", (_, _) => UniTask.CompletedTask));
        }

        [UnityTest]
        public IEnumerator WaitUntil_RechecksProviderAndStopsWhenConditionHolds()
        {
            var ready = false;
            _handlers.RegisterCondition("ready", _ => ready);
            var task = Run(new[] { new SequenceNode { Command = "wait_until", Arguments = new[] { "ready" } },
                SequenceNode.Action("record", "after") });
            yield return null;
            Assert.That(task.IsCompleted, Is.False);
            ready = true;
            for (var frame = 0; frame < 10 && !task.IsCompleted; frame++)
                yield return null;
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
            Assert.That(_log, Is.EqualTo(new[] { "after" }));
        }

        [Test]
        public void Race_CancelsLosingEventSubscriptionBeforeContinuing()
        {
            var first = new UniTaskCompletionSource();
            var second = new UniTaskCompletionSource();
            var active = 0;
            _handlers.RegisterWait("event", async (context, token) =>
            {
                active++;
                var signal = context.Arguments[0] == "first" ? first : second;
                try
                {
                    using var registration = token.Register(() => signal.TrySetCanceled(token));
                    await signal.Task;
                }
                finally
                {
                    active--;
                }
            });
            var task = Run(new[] { new SequenceNode { Kind = SequenceNodeKind.Race, Children = new[]
            {
                new SequenceNode { Command = "wait", Arguments = new[] { "event", "first" } },
                new SequenceNode { Command = "wait", Arguments = new[] { "event", "second" } },
            } }, SequenceNode.Action("record", "after") });
            Assert.That(active, Is.EqualTo(2));
            second.TrySetResult();
            task.GetAwaiter().GetResult();
            Assert.That(active, Is.Zero);
            Assert.That(_log, Is.EqualTo(new[] { "after" }));
        }

        private Task Run(SequenceNode[] body, SequenceNode[] cleanup = null, CancellationToken token = default)
            => _runner.RunStepAsync("sequence", "step", 3, body, cleanup, token).AsTask();
    }
}
