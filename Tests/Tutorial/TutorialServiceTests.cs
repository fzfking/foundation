using System;
using System.Linq;
using System.Threading;
using Foundation.Requirements;
using Foundation.Tutorial;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Tutorial
{
    public sealed class TutorialServiceTests
    {
        [Test]
        public void Run_ExecutesEveryStepInOrderAndCompletesTheChapter()
        {
            var first = new TestStep("first");
            var second = new TestStep("second");
            var flow = new Flow(first, second);

            flow.Run(Chapter("ftue", "first", "second"));

            Assert.That(first.ExecutionCount, Is.EqualTo(1));
            Assert.That(second.ExecutionCount, Is.EqualTo(1));
            Assert.That(flow.State.GetStatus(new TutorialChapterId("ftue")),
                Is.EqualTo(TutorialChapterStatus.Completed));
            Assert.That(flow.Service.IsRunning, Is.False);
        }

        [Test]
        public void Run_WaitsForTheStepTaskBeforeMarkingItComplete()
        {
            var first = new DeferredStep("first");
            var second = new TestStep("second");
            var flow = new Flow(first, second);
            var chapter = Chapter("ftue", "first", "second");

            var run = flow.Service.RunAsync(chapter);

            Assert.That(run.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(flow.Progress("ftue").CurrentStep.Value.Value, Is.EqualTo("first"));
            Assert.That(flow.Progress("ftue").CompletedSteps, Is.Empty);
            Assert.That(second.ExecutionCount, Is.Zero);

            first.Complete();
            run.GetAwaiter().GetResult();

            Assert.That(flow.Progress("ftue").CompletedSteps, Does.Contain(new TutorialStepId("first")));
            Assert.That(second.ExecutionCount, Is.EqualTo(1));
        }

        [Test]
        public void Run_ResumesAtTheFirstIncompleteStep()
        {
            var first = new TestStep("first");
            var second = new TestStep("second");
            var flow = new Flow(first, second);
            var progress = flow.State.GetOrCreate(new TutorialChapterId("ftue"));
            progress.Set(
                TutorialChapterStatus.InProgress,
                new TutorialStepId("second"),
                new[] { new TutorialStepId("first") });

            flow.Run(Chapter("ftue", "first", "second"));

            Assert.That(first.ExecutionCount, Is.Zero, "an already completed step must not replay");
            Assert.That(second.ExecutionCount, Is.EqualTo(1));
        }

        [Test]
        public void Run_CompletedChapter_DoesNothing()
        {
            var first = new TestStep("first");
            var flow = new Flow(first);
            var progress = flow.State.GetOrCreate(new TutorialChapterId("ftue"));
            progress.Set(TutorialChapterStatus.Completed, null, Array.Empty<TutorialStepId>());

            flow.Run(Chapter("ftue", "first"));

            Assert.That(first.ExecutionCount, Is.Zero);
        }

        [Test]
        public void Run_WhileAnotherChapterIsRunning_IsRejected()
        {
            var blocking = new DeferredStep("first");
            var flow = new Flow(blocking, new TestStep("other"));
            var running = flow.Service.RunAsync(Chapter("ftue", "first"));

            Assert.Throws<InvalidOperationException>(
                () => flow.Service.RunAsync(Chapter("second", "other")).GetAwaiter().GetResult());

            blocking.Complete();
            running.GetAwaiter().GetResult();
        }

        [Test]
        public void CaptureCheckpoint_DropsProgressBeforeTheFirstCheckpoint()
        {
            var second = new DeferredStep("second");
            var flow = new Flow(new TestStep("first"), second, new TestStep("third"));
            var chapter = Chapter("ftue", new[] { "first", "second", "third" }, new[] { "second" });
            var run = flow.Service.RunAsync(chapter);

            // "first" is done but the only checkpoint has not been reached yet.
            var captured = flow.Service.CaptureCheckpoint(chapter);

            Assert.That(captured.Status, Is.EqualTo(TutorialChapterStatus.NotStarted));
            Assert.That(captured.CompletedSteps, Is.Empty);
            Assert.That(captured.CurrentStep, Is.Null);

            second.Complete();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void CaptureCheckpoint_PreservesProgressThroughTheLastCheckpoint()
        {
            var third = new DeferredStep("third");
            var flow = new Flow(new TestStep("first"), new TestStep("second"), third);
            var chapter = Chapter("ftue", new[] { "first", "second", "third" }, new[] { "second" });
            var run = flow.Service.RunAsync(chapter);

            var captured = flow.Service.CaptureCheckpoint(chapter);

            Assert.That(captured.Status, Is.EqualTo(TutorialChapterStatus.InProgress));
            Assert.That(captured.CurrentStep.Value.Value, Is.EqualTo("third"));
            Assert.That(
                captured.CompletedSteps.Select(step => step.Value).OrderBy(value => value),
                Is.EqualTo(new[] { "first", "second" }));

            third.Complete();
            run.GetAwaiter().GetResult();
        }

        [Test]
        public void CaptureCheckpoint_CompletedChapter_StaysCompleted()
        {
            var flow = new Flow(new TestStep("first"));
            var chapter = Chapter("ftue", "first");
            flow.Run(chapter);

            var captured = flow.Service.CaptureCheckpoint(chapter);

            Assert.That(captured.Status, Is.EqualTo(TutorialChapterStatus.Completed));
        }

        [Test]
        public void ChaptersKeepSeparateStepProgress()
        {
            var flow = new Flow(new TestStep("first"), new TestStep("second"));

            flow.Run(Chapter("chapter-one", "first"));
            flow.Run(Chapter("chapter-two", "second"));

            Assert.That(
                flow.Progress("chapter-one").CompletedSteps.Select(step => step.Value),
                Is.EqualTo(new[] { "first" }));
            Assert.That(
                flow.Progress("chapter-two").CompletedSteps.Select(step => step.Value),
                Is.EqualTo(new[] { "second" }));
        }

        internal static TutorialChapterDefinition Chapter(string id, params string[] steps)
            => Chapter(id, steps, Array.Empty<string>());

        internal static TutorialChapterDefinition Chapter(
            string id,
            string[] steps,
            string[] checkpoints,
            int priority = 0,
            IRequirement requirement = null,
            string triggerId = "trigger")
            => new(
                new TutorialChapterId(id),
                new TutorialTriggerId(triggerId),
                steps.Select(step => new TutorialStepId(step)).ToArray(),
                checkpoints.Select(step => new TutorialStepId(step)).ToArray(),
                priority,
                requirement);

        internal sealed class Flow
        {
            public Flow(params ITutorialStep[] steps)
            {
                var registry = new TutorialRegistry();
                foreach (var step in steps)
                    registry.Register(step);

                State = new();
                Service = new(registry, State);
            }

            public TutorialProgressState State { get; }
            public TutorialService Service { get; }

            public void Run(TutorialChapterDefinition chapter)
                => Service.RunAsync(chapter).GetAwaiter().GetResult();

            public TutorialChapterProgress Progress(string chapterId)
                => State.GetOrCreate(new TutorialChapterId(chapterId));
        }

        internal sealed class TestStep : ITutorialStep
        {
            public TestStep(string id) => Id = new TutorialStepId(id);

            public TutorialStepId Id { get; }

            public int ExecutionCount { get; private set; }

            public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
            {
                ExecutionCount++;
                return UniTask.CompletedTask;
            }
        }

        internal sealed class DeferredStep : ITutorialStep
        {
            private readonly UniTaskCompletionSource _completion = new();

            public DeferredStep(string id) => Id = new TutorialStepId(id);

            public TutorialStepId Id { get; }

            public int ExecutionCount { get; private set; }

            public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExecutionCount++;
                return _completion.Task;
            }

            public void Complete() => _completion.TrySetResult();
        }
    }
}
