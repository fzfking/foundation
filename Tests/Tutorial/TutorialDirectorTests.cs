using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foundation.Requirements;
using Foundation.Tutorial;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Foundation.Tests.Tutorial
{
    public sealed class TutorialDirectorTests
    {
        private const string FirstTrigger = "trigger-one";
        private const string SecondTrigger = "trigger-two";

        [Test]
        public void TwoChaptersWithDifferentTriggers_RunIndependently()
        {
            var first = new TutorialServiceTests.TestStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var flow = new Flow(
                new[] { first, second },
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, "second"));

            flow.Raise(FirstTrigger);

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
            Assert.That(second.ExecutionCount, Is.Zero, "an unrelated trigger must not start the other chapter");

            flow.Raise(SecondTrigger);

            Assert.That(second.ExecutionCount, Is.EqualTo(1));
            Assert.That(flow.State.IsCompleted(new TutorialChapterId("chapter-one")), Is.True);
            Assert.That(flow.State.IsCompleted(new TutorialChapterId("chapter-two")), Is.True);
        }

        [Test]
        public void ChapterTriggeredWhileAnotherIsRunning_WaitsInsteadOfRunningInParallel()
        {
            var blocking = new TutorialServiceTests.DeferredStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var flow = new Flow(
                new ITutorialStep[] { blocking, second },
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, "second"));

            flow.Raise(FirstTrigger);
            flow.Raise(SecondTrigger);

            Assert.That(flow.Director.ActiveChapter.Value.Value, Is.EqualTo("chapter-one"));
            Assert.That(second.ExecutionCount, Is.Zero);
            Assert.That(flow.Director.Pending.Select(id => id.Value), Is.EqualTo(new[] { "chapter-two" }));

            blocking.Complete();

            Assert.That(second.ExecutionCount, Is.EqualTo(1), "the queued chapter runs once the active one finished");
            Assert.That(flow.Director.Pending, Is.Empty);
        }

        [Test]
        public void HigherPriorityChapterRunsFirst()
        {
            var order = new List<string>();
            var flow = new Flow(
                new ITutorialStep[] { new OrderedStep("low", order), new OrderedStep("high", order) },
                Chapter("low-priority", FirstTrigger, new[] { "low" }, priority: 1),
                Chapter("high-priority", FirstTrigger, new[] { "high" }, priority: 10));

            flow.Raise(FirstTrigger);

            Assert.That(order, Is.EqualTo(new[] { "high", "low" }));
        }

        [Test]
        public void EqualPriority_IsBrokenByChapterIdSoSelectionIsDeterministic()
        {
            var order = new List<string>();
            var flow = new Flow(
                new ITutorialStep[] { new OrderedStep("b-step", order), new OrderedStep("a-step", order) },
                // Queued in reverse alphabetical order on purpose: the outcome must not depend on it.
                Chapter("b-chapter", FirstTrigger, new[] { "b-step" }),
                Chapter("a-chapter", FirstTrigger, new[] { "a-step" }));

            flow.Raise(FirstTrigger);

            Assert.That(order, Is.EqualTo(new[] { "a-step", "b-step" }));
        }

        [Test]
        public void DuplicateTrigger_DoesNotQueueTheChapterTwice()
        {
            var blocking = new TutorialServiceTests.DeferredStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var flow = new Flow(
                new ITutorialStep[] { blocking, second },
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, "second"));

            flow.Raise(FirstTrigger);
            flow.Raise(SecondTrigger);
            flow.Raise(SecondTrigger);
            flow.Raise(SecondTrigger);

            Assert.That(flow.Director.Pending.Count, Is.EqualTo(1));

            blocking.Complete();

            Assert.That(second.ExecutionCount, Is.EqualTo(1), "a chapter must not replay because its trigger repeated");
        }

        [Test]
        public void TriggerForACompletedChapter_IsIgnored()
        {
            var first = new TutorialServiceTests.TestStep("first");
            var flow = new Flow(new[] { first }, Chapter("chapter-one", FirstTrigger, "first"));

            flow.Raise(FirstTrigger);
            flow.Raise(FirstTrigger);

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
            Assert.That(flow.Director.Pending, Is.Empty);
        }

        [Test]
        public void ChapterWithAnUnmetRequirement_StaysPendingInsteadOfRunning()
        {
            var gate = new ToggleRequirement { IsMet = false };
            var first = new TutorialServiceTests.TestStep("first");
            var flow = new Flow(
                new[] { first },
                Chapter("chapter-one", FirstTrigger, new[] { "first" }, requirement: gate));

            flow.Raise(FirstTrigger);

            Assert.That(first.ExecutionCount, Is.Zero);
            Assert.That(flow.Director.Pending.Select(id => id.Value), Is.EqualTo(new[] { "chapter-one" }));

            gate.IsMet = true;
            flow.Director.TryStartNextAsync().GetAwaiter().GetResult();

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
        }

        [Test]
        public void RequirementThatStopsHoldingWhilePending_BlocksTheStart()
        {
            var gate = new ToggleRequirement { IsMet = true };
            var blocking = new TutorialServiceTests.DeferredStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var flow = new Flow(
                new ITutorialStep[] { blocking, second },
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, new[] { "second" }, requirement: gate));

            flow.Raise(FirstTrigger);
            flow.Raise(SecondTrigger);
            Assert.That(flow.Director.Pending.Select(id => id.Value), Is.EqualTo(new[] { "chapter-two" }));

            // The world changed while the chapter waited its turn.
            gate.IsMet = false;
            blocking.Complete();

            Assert.That(second.ExecutionCount, Is.Zero, "requirements are re-checked immediately before starting");
            Assert.That(flow.Director.Pending.Select(id => id.Value), Is.EqualTo(new[] { "chapter-two" }),
                "the chapter is not dropped, it simply did not start");
        }

        [Test]
        public void ChapterCompletedRequirement_LetsASecondChapterFollowTheFirst()
        {
            var first = new TutorialServiceTests.TestStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var state = new TutorialProgressState();
            var requirement = new TutorialChapterCompletedRequirement(state, new TutorialChapterId("chapter-one"));
            var flow = new Flow(
                new ITutorialStep[] { first, second },
                state,
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, new[] { "second" }, requirement: requirement));

            flow.Raise(SecondTrigger);
            Assert.That(second.ExecutionCount, Is.Zero, "the follow-up chapter waits for the first one");

            flow.Raise(FirstTrigger);

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
            Assert.That(second.ExecutionCount, Is.EqualTo(1),
                "finishing the prerequisite chapter releases the pending one");
        }

        [Test]
        public void RegisteredTriggersRaiseThroughTheDirector()
        {
            var first = new TutorialServiceTests.TestStep("first");
            var trigger = new ManualTrigger(new TutorialTriggerId(FirstTrigger));
            var flow = new Flow(
                new[] { first },
                new TutorialProgressState(),
                new[] { trigger },
                Chapter("chapter-one", FirstTrigger, "first"));

            trigger.Raise();

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RaiseAndWait_ReturnsOnlyAfterTheRunningChapterAndTheQueuedOneFinished() => UniTask.ToCoroutine(async () =>
        {
            var blocking = new TutorialServiceTests.DeferredStep("first");
            var second = new TutorialServiceTests.TestStep("second");
            var flow = new Flow(
                new ITutorialStep[] { blocking, second },
                Chapter("chapter-one", FirstTrigger, "first"),
                Chapter("chapter-two", SecondTrigger, "second"));
            flow.Raise(FirstTrigger);

            var wait = flow.Director.RaiseAndWaitAsync(new TutorialTriggerId(SecondTrigger)).Preserve();
            await UniTask.Yield();

            Assert.That(wait.Status, Is.EqualTo(UniTaskStatus.Pending), "a chapter is still running");
            Assert.That(second.ExecutionCount, Is.Zero);

            blocking.Complete();
            await wait;

            Assert.That(second.ExecutionCount, Is.EqualTo(1), "the queued chapter ran before the wait returned");
            Assert.That(flow.Service.IsRunning, Is.False);
        });

        [UnityTest]
        public IEnumerator WaitForIdle_ReturnsAfterAChapterStartedByAnEventHasFinished() => UniTask.ToCoroutine(async () =>
        {
            var blocking = new TutorialServiceTests.DeferredStep("first");
            var flow = new Flow(new ITutorialStep[] { blocking }, Chapter("chapter-one", FirstTrigger, "first"));
            flow.Raise(FirstTrigger);

            var wait = flow.Director.WaitForIdleAsync().Preserve();
            await UniTask.Yield();
            Assert.That(wait.Status, Is.EqualTo(UniTaskStatus.Pending));

            blocking.Complete();
            await wait;

            Assert.That(flow.Service.IsRunning, Is.False);
        });

        internal static TutorialChapterDefinition Chapter(string id, string trigger, params string[] steps)
            => Chapter(id, trigger, steps, 0);

        internal static TutorialChapterDefinition Chapter(
            string id,
            string trigger,
            string[] steps,
            int priority = 0,
            IRequirement requirement = null,
            string[] checkpoints = null)
            => new(
                new TutorialChapterId(id),
                new TutorialTriggerId(trigger),
                steps.Select(step => new TutorialStepId(step)).ToArray(),
                (checkpoints ?? Array.Empty<string>()).Select(step => new TutorialStepId(step)).ToArray(),
                priority,
                requirement);

        internal sealed class Flow
        {
            public Flow(IEnumerable<ITutorialStep> steps, params TutorialChapterDefinition[] chapters)
                : this(steps, new TutorialProgressState(), chapters)
            {
            }

            public Flow(
                IEnumerable<ITutorialStep> steps,
                TutorialProgressState state,
                params TutorialChapterDefinition[] chapters)
                : this(steps, state, Array.Empty<ITutorialTrigger>(), chapters)
            {
            }

            public Flow(
                IEnumerable<ITutorialStep> steps,
                TutorialProgressState state,
                IEnumerable<ITutorialTrigger> triggers,
                params TutorialChapterDefinition[] chapters)
            {
                var registry = new TutorialRegistry();
                foreach (var step in steps)
                    registry.Register(step);

                State = state;
                Service = new(registry, State);
                Catalog = new(new StubChapterSource(chapters));
                Director = new(Catalog, Service, State, triggers);
                Director.InitializeAsync().GetAwaiter().GetResult();
            }

            public TutorialProgressState State { get; }
            public TutorialService Service { get; }
            public TutorialChapterCatalog Catalog { get; }
            public TutorialDirector Director { get; }

            public void Raise(string triggerId) => Director.Raise(new TutorialTriggerId(triggerId));
        }

        internal sealed class StubChapterSource : ITutorialChapterSource
        {
            private readonly TutorialChapterDefinition[] _chapters;

            public StubChapterSource(TutorialChapterDefinition[] chapters) => _chapters = chapters;

            public UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(
                CancellationToken cancellationToken = default)
                => UniTask.FromResult<IReadOnlyList<TutorialChapterDefinition>>(_chapters);
        }

        private sealed class ManualTrigger : ITutorialTrigger
        {
            private ITutorialTriggerSink _sink;

            public ManualTrigger(TutorialTriggerId id) => Id = id;

            public TutorialTriggerId Id { get; }

            public void Activate(ITutorialTriggerSink sink) => _sink = sink;

            public void Raise() => _sink?.Raise(Id);

            public void Dispose() => _sink = null;
        }

        private sealed class ToggleRequirement : IRequirement
        {
            public bool IsMet { get; set; }

            public RequirementResult Evaluate()
                => IsMet ? RequirementResult.Satisfied : RequirementResult.Failed("test.blocked");
        }

        [Test]
        public void FailedChapter_DoesNotRetryOnLaterTriggersAndDoesNotBlockOtherChapters()
        {
            var failing = new FailingStep();
            var other = new TutorialServiceTests.TestStep("other");
            var flow = new Flow(new ITutorialStep[] { failing, other },
                Chapter("broken", FirstTrigger, new[] { "broken-step" }, priority: 10),
                Chapter("healthy", FirstTrigger, new[] { "other" }));

            flow.Raise(FirstTrigger);
            flow.Raise(FirstTrigger);
            flow.Director.ResumeAsync().GetAwaiter().GetResult();

            Assert.That(failing.Attempts, Is.EqualTo(3));
            Assert.That(flow.State.IsCompleted(new TutorialChapterId("healthy")), Is.True);
        }

        private sealed class FailingStep : ITutorialStep
        {
            public int Attempts;
            public TutorialStepId Id => new("broken-step");
            public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
            {
                Attempts++;
                throw new InvalidOperationException("Broken chapter.");
            }
        }

        private sealed class OrderedStep : ITutorialStep
        {
            private readonly List<string> _order;

            public OrderedStep(string id, List<string> order)
            {
                Id = new TutorialStepId(id);
                _order = order;
            }

            public TutorialStepId Id { get; }

            public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
            {
                _order.Add(Id.Value);
                return UniTask.CompletedTask;
            }
        }
    }
}
