using System.Collections.Generic;
using System.Linq;
using System;
using Foundation.Tutorial;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Tutorial
{
    public sealed class TutorialSaveTests
    {
        private const string FirstTrigger = "trigger-one";
        private const string SecondTrigger = "trigger-two";

        [Test]
        public void Capture_KeepsChapterProgressSeparate()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();
            flow.Raise(SecondTrigger);

            var snapshot = Module(flow).Capture();

            var one = snapshot.Chapters.Single(chapter => chapter.ChapterId == "chapter-one");
            var two = snapshot.Chapters.Single(chapter => chapter.ChapterId == "chapter-two");
            Assert.That(one.Status, Is.EqualTo((int)TutorialChapterStatus.Completed));
            Assert.That(one.CompletedStepIds, Is.EqualTo(new[] { "one-a", "one-b" }));
            Assert.That(two.CompletedStepIds, Is.EqualTo(new[] { "two-a" }),
                "one chapter's steps must never leak into another's progress");
        }

        [Test]
        public void RestoreInProgressChapter_ResumesAtTheStoredStep()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            var snapshot = Module(flow).Capture();
            blocking.Complete();

            var reloaded = NewFlow(out var reloadedBlocking);
            Module(reloaded).Restore(snapshot);
            reloaded.Raise(FirstTrigger);

            Assert.That(reloaded.Steps["one-a"].ExecutionCount, Is.EqualTo(1),
                "progress stopped at the checkpoint, so the first step replays");
            Assert.That(reloaded.Director.ActiveChapter.Value.Value, Is.EqualTo("chapter-one"));
            reloadedBlocking.Complete();
        }

        [Test]
        public void CheckpointRollback_DropsProgressMadeAfterTheLastCheckpoint()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);

            // "one-a" is done; the chapter is parked on "one-b", which is the checkpoint.
            var snapshot = Module(flow).Capture();

            var chapter = snapshot.Chapters.Single(entry => entry.ChapterId == "chapter-one");
            Assert.That(chapter.Status, Is.EqualTo((int)TutorialChapterStatus.NotStarted));
            Assert.That(chapter.CompletedStepIds, Is.Empty);
            blocking.Complete();
        }

        [Test]
        public void CompletedChapter_DoesNotReplayAfterReload()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();
            var snapshot = Module(flow).Capture();

            var reloaded = NewFlow(out _);
            Module(reloaded).Restore(snapshot);
            reloaded.Raise(FirstTrigger);

            Assert.That(reloaded.Steps["one-a"].ExecutionCount, Is.Zero);
            Assert.That(reloaded.Director.Pending, Is.Empty);
        }

        [Test]
        public void ChapterThatWasNeverTriggered_IsStillAvailableWhenItsTriggerFiresAfterAReload()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();
            var snapshot = Module(flow).Capture();

            var reloaded = NewFlow(out _);
            Module(reloaded).Restore(snapshot);
            Assert.That(reloaded.Director.Pending, Is.Empty);

            reloaded.Raise(SecondTrigger);

            Assert.That(reloaded.Steps["two-a"].ExecutionCount, Is.EqualTo(1));
        }

        [Test]
        public void NeverTriggeredChapter_DoesNotStartAfterReload_EvenWithSatisfiedRequirements()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();
            var snapshot = Module(flow).Capture();

            var reloaded = NewFlow(out _);
            Module(reloaded).Restore(snapshot);
            reloaded.Resume();

            Assert.That(reloaded.Steps["two-a"].ExecutionCount, Is.Zero,
                "chapter-two has no requirement at all, and still must not run: its trigger never fired");
            Assert.That(reloaded.Director.Pending, Is.Empty);
        }

        [Test]
        public void PendingChapterThatCouldNotStart_ResumesAfterReloadWithoutANewTrigger()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            flow.Raise(SecondTrigger);
            Assert.That(flow.Director.Pending.Select(id => id.Value), Is.EqualTo(new[] { "chapter-two" }));

            // The player quits while chapter-two is still waiting behind chapter-one.
            var snapshot = Module(flow).Capture();
            blocking.Complete();

            var reloaded = NewFlow(out var reloadedBlocking);
            Module(reloaded).Restore(snapshot);

            Assert.That(reloaded.Director.Pending.Select(id => id.Value),
                Does.Contain("chapter-two"), "a chapter that was already waiting is still waiting");

            reloaded.Resume();

            // chapter-one was rolled back to its checkpoint and resumes first; chapter-two follows.
            Assert.That(reloaded.Director.ActiveChapter.Value.Value, Is.EqualTo("chapter-one"));
            reloadedBlocking.Complete();

            Assert.That(reloaded.Steps["two-a"].ExecutionCount, Is.EqualTo(1));
        }

        [Test]
        public void InterruptedChapter_ResumesAfterReloadWithoutANewTrigger()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            var snapshot = Module(flow).Capture();
            blocking.Complete();

            var reloaded = NewFlow(out var reloadedBlocking);
            Module(reloaded).Restore(snapshot);
            reloaded.Resume();

            Assert.That(reloaded.Director.ActiveChapter.Value.Value, Is.EqualTo("chapter-one"));
            reloadedBlocking.Complete();
            Assert.That(reloaded.State.IsCompleted(new TutorialChapterId("chapter-one")), Is.True);
        }

        [Test]
        public void CandidacyIsClearedWhenAChapterCompletes()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();

            var chapter = Module(flow).Capture().Chapters.Single(entry => entry.ChapterId == "chapter-one");

            Assert.That(chapter.IsPending, Is.False);
            Assert.That(chapter.Status, Is.EqualTo((int)TutorialChapterStatus.Completed));
        }

        [Test]
        public void RestoreReplacesProgressInPlace()
        {
            var flow = NewFlow(out var blocking);
            flow.Raise(FirstTrigger);
            blocking.Complete();
            var state = flow.State;

            Module(flow).Restore(new TutorialSnapshot
            {
                Chapters = new[]
                {
                    new TutorialChapterSnapshot
                    {
                        ChapterId = "chapter-one",
                        Status = (int)TutorialChapterStatus.NotStarted,
                        CompletedStepIds = Array.Empty<string>()
                    }
                }
            });

            Assert.That(ReferenceEquals(state, flow.State), Is.True);
            Assert.That(flow.State.IsCompleted(new TutorialChapterId("chapter-one")), Is.False);
        }

        private static TutorialSaveModule Module(SaveFlow flow)
            => new(flow.State, flow.Catalog, flow.Service);

        private static SaveFlow NewFlow(out TutorialServiceTests.DeferredStep blocking)
        {
            blocking = new TutorialServiceTests.DeferredStep("one-b");
            return new SaveFlow(blocking);
        }

        private sealed class SaveFlow
        {
            private readonly TutorialDirectorTests.Flow _flow;

            public SaveFlow(TutorialServiceTests.DeferredStep blocking)
            {
                Steps = new Dictionary<string, TutorialServiceTests.TestStep>
                {
                    ["one-a"] = new("one-a"),
                    ["two-a"] = new("two-a")
                };

                _flow = new(
                    new ITutorialStep[] { Steps["one-a"], blocking, Steps["two-a"] },
                    TutorialDirectorTests.Chapter(
                        "chapter-one",
                        FirstTrigger,
                        new[] { "one-a", "one-b" },
                        checkpoints: new[] { "one-b" }),
                    TutorialDirectorTests.Chapter("chapter-two", SecondTrigger, "two-a"));
            }

            public Dictionary<string, TutorialServiceTests.TestStep> Steps { get; }

            public TutorialProgressState State => _flow.State;
            public TutorialChapterCatalog Catalog => _flow.Catalog;
            public TutorialService Service => _flow.Service;
            public TutorialDirector Director => _flow.Director;

            public void Raise(string triggerId) => _flow.Raise(triggerId);

            public void Resume() => Director.ResumeAsync().Forget();
        }
    }
}
