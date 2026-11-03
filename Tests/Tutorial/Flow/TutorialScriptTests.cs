using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Foundation.Config;
using Foundation.Tutorial;
using Foundation.Tutorial.Flow;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Tutorial.Flow
{
    public sealed class TutorialScriptTests
    {
        private const string ValidChapter = @"
# comment line
chapter tutorial.test
trigger screen-ready
priority 3

step tutorial.test.first checkpoint
  skip_if not test.is on
  say ""tutorial.key with space"" continue=none side=above   # trailing comment
  if not test.is off
    highlight a.buy hand=off
  else
    highlight b.buy
  end
  race
    wait test.signal a
    wait test.signal b
  end
  finally
    action test.log cleanup
end

step tutorial.test.last
  close
end
";

        [Test]
        public void Parse_ValidChapter_BuildsHeaderStepsBlocksAndArguments()
        {
            var errors = new List<TutorialScriptError>();

            var config = TutorialScriptParser.Parse(ValidChapter, errors);

            Assert.That(errors, Is.Empty);
            Assert.That(config.Id, Is.EqualTo("tutorial.test"));
            Assert.That(config.TriggerId, Is.EqualTo("screen-ready"));
            Assert.That(config.Priority, Is.EqualTo(3));
            Assert.That(config.Steps.Select(step => step.IsCheckpoint), Is.EqualTo(new[] { true, false }));

            var body = config.Steps[0].Body;
            Assert.That(body.Select(node => node.Kind), Is.EqualTo(new[]
            {
                TutorialNodeKind.Command, TutorialNodeKind.Command, TutorialNodeKind.If, TutorialNodeKind.Race,
            }));
            Assert.That(body[0].Arguments, Is.EqualTo(new[] { "not", "test.is", "on" }));
            Assert.That(body[1].Arguments, Is.EqualTo(new[] { "tutorial.key with space" }));
            Assert.That(body[1].GetNamed("continue"), Is.EqualTo("none"));
            Assert.That(body[1].GetNamed("side"), Is.EqualTo("above"));
            Assert.That(body[2].Negated, Is.True);
            Assert.That(body[2].Children.Single().Arguments, Is.EqualTo(new[] { "a.buy" }));
            Assert.That(body[2].ElseChildren.Single().Arguments, Is.EqualTo(new[] { "b.buy" }));
            Assert.That(body[3].Children, Has.Length.EqualTo(2));
            Assert.That(config.Steps[0].Finally.Single().Arguments, Is.EqualTo(new[] { "test.log", "cleanup" }));
        }

        [Test]
        public void Parse_CheckpointsAll_MarksEveryStep()
        {
            var errors = new List<TutorialScriptError>();

            var config = TutorialScriptParser.Parse(
                "chapter c\ntrigger t\ncheckpoints all\nstep one\n  close\nend\nstep two\n  close\nend\n", errors);

            Assert.That(errors, Is.Empty);
            Assert.That(config.Steps.All(step => step.IsCheckpoint), Is.True);
        }

        [TestCase("  else\n", 5, TestName = "Parse_ElseOutsideIf_ReportsItsLine")]
        [TestCase("  race\n    close\n  end\n", 5, TestName = "Parse_RaceWithOneBranch_ReportsRaceLine")]
        [TestCase("  say \"unterminated\n", 5, TestName = "Parse_UnterminatedQuote_ReportsItsLine")]
        [TestCase("  if\n  end\n", 5, TestName = "Parse_IfWithoutCondition_ReportsItsLine")]
        [TestCase("  say key continue=tap continue=none\n", 5, TestName = "Parse_DuplicateNamedArgument_ReportsItsLine")]
        [TestCase("  say side=above key\n", 5, TestName = "Parse_PositionalAfterNamed_ReportsItsLine")]
        public void Parse_MalformedBlock_ReportsErrorAtLine(string stepLines, int expectedLine)
        {
            var errors = new List<TutorialScriptError>();

            TutorialScriptParser.Parse("chapter c\ntrigger t\n\nstep s\n" + stepLines + "  close\nend\n", errors);

            Assert.That(errors.Select(error => error.Line), Does.Contain(expectedLine), string.Join("\n", errors));
        }

        [Test]
        public void Parse_MissingEndHeaderAndDuplicateStep_AreAllReported()
        {
            var errors = new List<TutorialScriptError>();

            TutorialScriptParser.Parse("step s\n  close\nend\nstep s\n  close\n", errors);

            var messages = string.Join("\n", errors);
            Assert.That(messages, Does.Contain("no 'chapter"));
            Assert.That(messages, Does.Contain("no 'trigger"));
            Assert.That(messages, Does.Contain("more than once"));
            Assert.That(messages, Does.Contain("missing its closing 'end'"));
        }

        [TestCase("  frobnicate", TestName = "Validate_UnknownPrimitive_IsReported")]
        [TestCase("  action test.missing", TestName = "Validate_UnknownGameKey_IsReported")]
        [TestCase("  wait test.is on", TestName = "Validate_KeyFromWrongCategory_IsReported")]
        [TestCase("  wait test.signal", TestName = "Validate_MissingRequiredParameter_IsReported")]
        [TestCase("  action test.log a b", TestName = "Validate_TooManyParameters_IsReported")]
        [TestCase("  say key side=diagonal", TestName = "Validate_UnknownSide_IsReported")]
        [TestCase("  say key speed=2", TestName = "Validate_UnknownNamedArgument_IsReported")]
        [TestCase("  delay -1", TestName = "Validate_NegativeDelay_IsReported")]
        [TestCase("  highlight", TestName = "Validate_HighlightWithoutTarget_IsReported")]
        [TestCase("  delay NaN", TestName = "Validate_NonFiniteDelay_IsReported")]
        [TestCase("  highlight a.buy timeout=-1", TestName = "Validate_NegativeTimeout_IsReported")]
        public void Validate_InvalidCommand_ReportsErrorAtItsLine(string line)
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n" + line + "\n  close\nend\n");

            Assert.That(errors.Select(error => error.Line), Does.Contain(4), string.Join("\n", errors));
        }

        [Test]
        public void Validate_OptionalParameterAndNotPrefix_AreAccepted()
        {
            var errors = Validate(
                "chapter c\ntrigger t\nstep s\n  skip_if not test.is on\n  wait_until test.is off\n  action test.log\n  finally\n    close\nend\n");

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void Validate_LastStepWithoutClose_IsReported()
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n  say key\nend\n");

            Assert.That(string.Join("\n", errors), Does.Contain("never runs 'close'"));
        }

        [Test]
        public void Validate_CloseInLastStepFinally_IsAccepted()
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n  say key\n  finally\n    close\nend\n");

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [TestCase("if test.is on\n    close\n  end\n", false, TestName = "Validate_CloseInOnlyOneIfBranch_IsReported")]
        [TestCase("if test.is on\n    close\n  else\n    close\n  end\n", true, TestName = "Validate_CloseInBothIfBranches_IsAccepted")]
        [TestCase("race\n    wait test.signal one\n    close\n  end\n", false, TestName = "Validate_RaceWithNonClosingWinner_IsReported")]
        [TestCase("race\n    close\n    close\n  end\n", true, TestName = "Validate_AllRaceBranchesClose_IsAccepted")]
        public void Validate_LastStepMustCloseOnEveryPossiblePath(string block, bool accepted)
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n  " + block + "end\n");

            Assert.That(errors.Any(error => error.Message.Contains("never runs 'close'")), Is.EqualTo(!accepted),
                string.Join("\n", errors));
        }

        [TestCase("  close\n  say key\n", false, TestName = "Validate_CloseBeforeReopeningTheOverlay_IsReported")]
        [TestCase("  skip_if test.is on\n  close\n", false, TestName = "Validate_CloseAfterSkipIf_IsNotGuaranteed")]
        [TestCase("  skip_if test.is on\n  finally\n    close\n", true, TestName = "Validate_FinallyCloseAfterSkipIf_IsAccepted")]
        [TestCase("  close\n  skip_if test.is on\n", true, TestName = "Validate_CloseBeforeSkipIf_IsAccepted")]
        [TestCase("  close\n  finally\n    say key\n", false, TestName = "Validate_FinallyCanReopenAfterBodyClose_IsReported")]
        public void Validate_LastStepAccountsForCommandOrderAndEarlyExit(string body, bool accepted)
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n" + body + "end\n");

            Assert.That(errors.Any(error => error.Message.Contains("never runs 'close'")), Is.EqualTo(!accepted),
                string.Join("\n", errors));
        }

        [Test]
        public void Validate_UnknownTextKey_IsReportedForTextAndSpeaker()
        {
            var errors = new List<TutorialScriptError>();
            var config = TutorialScriptParser.Parse(
                "chapter c\ntrigger t\nstep s\n  say known speaker=missing.speaker\n  say missing.text\n  close\nend\n", errors);

            TutorialScriptValidator.Validate(config, TutorialTestCommands.Catalog, key => key == "known", errors);

            Assert.That(errors.Select(error => error.Line), Is.EquivalentTo(new[] { 4, 5 }), string.Join("\n", errors));
        }

        [Test]
        public void Validate_WaitClickWithTimeout_IsAccepted()
        {
            var errors = Validate("chapter c\ntrigger t\nstep s\n  wait_click a.buy timeout=0.5\n  close\nend\n");

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
        }

        [Test]
        public void FromTypes_RepeatedCommandKey_IsRejectedInsteadOfDropped()
        {
            Assert.That(
                () => TutorialCommandCatalog.FromTypes(new[] { typeof(IsCondition), typeof(IsCondition) }),
                Throws.InvalidOperationException.With.Message.Contains("'test.is' is declared more than once"));
        }

        [Test]
        public void ChapterSource_RejectsRepeatedStepIdsAndAllowsIdempotentReloads()
        {
            var first = Script("chapter-a", "same-step");
            var duplicate = Script("chapter-b", "same-step");
            var presentation = new FakePresentation();
            var runner = new TutorialScriptRunner(
                presentation, presentation, presentation, new TutorialTargetRegistry(), new TutorialCommandRegistry());
            var registry = new TutorialRegistry();
            var duplicateSource = new ScriptTutorialChapterSource(
                new StaticConfigResolver(new[] { first, duplicate }), registry, runner);

            Assert.Throws<InvalidOperationException>(() => duplicateSource.GetChaptersAsync().GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => registry.Resolve(new TutorialStepId("same-step")));

            var source = new ScriptTutorialChapterSource(
                new StaticConfigResolver(new[] { first }), new TutorialRegistry(), runner);
            Assert.That(source.GetChaptersAsync().GetAwaiter().GetResult(), Has.Count.EqualTo(1));
            Assert.That(source.GetChaptersAsync().GetAwaiter().GetResult(), Has.Count.EqualTo(1));
        }

        private static List<TutorialScriptError> Validate(string script)
        {
            var errors = new List<TutorialScriptError>();
            var config = TutorialScriptParser.Parse(script, errors);
            Assert.That(errors, Is.Empty, "The test script must parse: " + string.Join("\n", errors));
            TutorialScriptValidator.Validate(config, TutorialTestCommands.Catalog, null, errors);
            return errors;
        }

        private static TutorialScriptConfig Script(string chapterId, string stepId)
            => new()
            {
                Id = chapterId,
                TriggerId = "trigger-" + chapterId,
                Steps = new[] { new TutorialStepScript { Id = stepId } },
            };

        private sealed class StaticConfigResolver : IConfigResolver
        {
            private readonly IReadOnlyList<TutorialScriptConfig> _scripts;
            public StaticConfigResolver(IReadOnlyList<TutorialScriptConfig> scripts) => _scripts = scripts;

            public UniTask<TConfig> ResolveAsync<TConfig>(string configId, CancellationToken cancellationToken = default)
                => throw new NotSupportedException();

            public UniTask<IReadOnlyList<TConfig>> ResolveAllAsync<TConfig>(CancellationToken cancellationToken = default)
                => UniTask.FromResult((IReadOnlyList<TConfig>)(object)_scripts);
        }
    }
}
