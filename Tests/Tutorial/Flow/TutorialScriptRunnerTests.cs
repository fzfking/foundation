using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Foundation.Tutorial.Flow;
using Foundation.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using System;

namespace Foundation.Tests.Tutorial.Flow
{
    public sealed class TutorialScriptRunnerTests
    {
        private List<string> _log;
        private SignalWait _signals;
        private FakePresentation _presentation;
        private TutorialTargetRegistry _targets;
        private RecordingStepObserver _observer;
        private TutorialScriptRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _log = new();
            _signals = new();
            _presentation = new();
            _targets = new();
            _observer = new();
            var commands = new TutorialCommandRegistry(
                new ITutorialAction[] { new LogAction(_log) },
                new ITutorialCondition[] { new IsCondition() },
                new ITutorialWait[] { _signals });
            _runner = new(_presentation, _presentation, _presentation, _targets, commands, new[] { _observer });
        }

        [Test]
        public void SkipIf_WhenConditionHolds_EndsStepNormally_AndStillRunsFinally()
        {
            var task = Run(
                "  skip_if test.is on\n  action test.log skipped\n  finally\n    action test.log cleanup\n");

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_log, Is.EqualTo(new[] { "cleanup" }));
            Assert.That(_observer.Completed, Is.EqualTo(new[] { "s" }));
        }

        [TestCase("if test.is on", "then", TestName = "If_TrueCondition_RunsThenBranch")]
        [TestCase("if test.is off", "else", TestName = "If_FalseCondition_RunsElseBranch")]
        [TestCase("if not test.is off", "then", TestName = "IfNot_FalseCondition_RunsThenBranch")]
        public void If_RunsExactlyOneBranch(string header, string expected)
        {
            var task = Run($"  {header}\n    action test.log then\n  else\n    action test.log else\n  end\n");

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_log, Is.EqualTo(new[] { expected }));
        }

        [Test]
        public void Race_FirstBranchToFinishWins_AndLosingBranchIsCancelled()
        {
            var task = Run(
                "  race\n    if test.is on\n      wait test.signal a\n      action test.log a\n    end\n"
                + "    if test.is on\n      wait test.signal b\n      action test.log b\n    end\n  end\n"
                + "  action test.log after\n");
            Assert.That(_signals.Pending, Is.EqualTo(2));

            _signals.Raise("b");

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_log, Is.EqualTo(new[] { "b", "after" }));
            Assert.That(_signals.Pending, Is.Zero, "The losing branch must stop waiting.");

            _signals.Raise("a");
            Assert.That(_log, Is.EqualTo(new[] { "b", "after" }));
        }

        [Test]
        public void Cancellation_MidStep_ClearsFocus_RunsFinally_AndDoesNotReportCompletion()
        {
            _targets.Register(new FakeTarget("prod.buy"));
            using var cancellation = new CancellationTokenSource();
            var task = Run(
                "  highlight prod.buy\n  wait test.signal never\n  action test.log unreachable\n  finally\n    action test.log cleanup\n",
                cancellation.Token);
            Assert.That(_presentation.Focused?.Id, Is.EqualTo("prod.buy"));

            cancellation.Cancel();

            // UniTask.AsTask reports a cancelled UniTask as a faulted Task carrying the cancellation.
            Assert.That(task.Exception?.InnerException, Is.InstanceOf<OperationCanceledException>());
            Assert.That(_presentation.Focused, Is.Null);
            Assert.That(_log, Is.EqualTo(new[] { "cleanup" }));
            Assert.That(_observer.Started, Is.EqualTo(new[] { "s" }));
            Assert.That(_observer.Completed, Is.Empty);
        }

        [Test]
        public void StepEnd_ClearsFocus_ButKeepsOverlayOpen()
        {
            _targets.Register(new FakeTarget("prod.buy"));

            Run("  highlight prod.buy\n  say key continue=none\n");

            Assert.That(_presentation.Focused, Is.Null);
            Assert.That(_presentation.OpenCount, Is.EqualTo(1), "The overlay opens once per step, not per command.");
            Assert.That(_presentation.CloseCount, Is.Zero);
        }

        [Test]
        public void OverlayThatCannotOpen_EndsStepWithoutRunningTheRest()
        {
            _presentation.CanOpen = false;

            var task = Run("  say key\n  action test.log after\n");

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_log, Is.Empty);
            Assert.That(_presentation.Dialogues, Is.Empty);
            Assert.That(_observer.Completed, Is.EqualTo(new[] { "s" }));
        }

        [Test]
        public void Say_WithTarget_AnchorsThere_WithoutTarget_FollowsFocus()
        {
            _targets.Register(new FakeTarget("resource.gold"));

            Run("  say first target=resource.gold side=below arrow=off\n  say second continue=none\n");

            var anchored = _presentation.Dialogues[0];
            Assert.That(anchored.Anchor?.Id, Is.EqualTo("resource.gold"));
            Assert.That(anchored.FollowFocus, Is.False);
            Assert.That(anchored.Side, Is.EqualTo(TooltipSide.Below));
            Assert.That(anchored.ShowArrow, Is.False);
            Assert.That(anchored.WaitForContinue, Is.True);

            var following = _presentation.Dialogues[1];
            Assert.That(following.Anchor, Is.Null);
            Assert.That(following.FollowFocus, Is.True);
            Assert.That(following.WaitForContinue, Is.False);
        }

        [Test]
        public void WaitClick_CompletesOnlyWhenThatTargetIsClicked()
        {
            var target = new FakeTarget("tab.managers");
            _targets.Register(target);

            var task = Run("  wait_click tab.managers\n  action test.log clicked\n");
            Assert.That(task.IsCompleted, Is.False);

            target.Click();

            Assert.That(task.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(_log, Is.EqualTo(new[] { "clicked" }));
        }

        // A target that never appears is skipped after its timeout, so the chapter goes on instead of waiting forever.
        [UnityTest]
        public IEnumerator WaitClick_WhenTheTargetNeverAppears_ContinuesAfterItsTimeout()
        {
            var task = Run("  wait_click tab.missing timeout=0.1\n  action test.log continued\n");

            var deadline = Time.realtimeSinceStartup + 5f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.That(task.IsCompleted, Is.True, "the chapter does not hang on a target that never appears");
            Assert.That(_log, Is.EqualTo(new[] { "continued" }));
        }

        [Test]
        public void Close_ClosesOverlay_AndNextDialogueReopensIt()
        {
            Run("  say one\n  close\n  say two\n");

            Assert.That(_presentation.CloseCount, Is.EqualTo(1));
            Assert.That(_presentation.OpenCount, Is.EqualTo(2));
        }

        private Task Run(string stepLines, CancellationToken cancellationToken = default)
        {
            var errors = new List<TutorialScriptError>();
            var config = TutorialScriptParser.Parse("chapter c\ntrigger t\nstep s\n" + stepLines + "end\n", errors);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));

            return _runner.RunAsync(config.Id, config.Steps[0], 0, cancellationToken).AsTask();
        }
    }
}
