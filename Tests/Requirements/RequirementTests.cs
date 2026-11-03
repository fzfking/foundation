using System.Collections.Generic;
using System.Linq;
using Foundation.Requirements;
using NUnit.Framework;

namespace Foundation.Tests.Requirements
{
    public sealed class RequirementTests
    {
        [Test]
        public void All_WhenEveryChildPasses_IsSatisfied()
        {
            var requirement = Requirement.All(Pass(), Pass());

            Assert.That(requirement.Evaluate().IsSatisfied, Is.True);
        }

        [Test]
        public void All_ReportsEveryFailure_SoUiCanListAllMissingSteps()
        {
            var requirement = Requirement.All(Fail("a"), Pass(), Fail("b"));

            var result = requirement.Evaluate();

            Assert.That(result.IsSatisfied, Is.False);
            Assert.That(result.Failures.Select(failure => failure.Code), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(result.FailureCode, Is.EqualTo("a"));
        }

        [Test]
        public void All_WithNoChildren_IsSatisfied()
        {
            Assert.That(Requirement.All().Evaluate().IsSatisfied, Is.True);
        }

        [Test]
        public void Any_IsSatisfiedByASingleSuccess()
        {
            var requirement = Requirement.Any(Fail("a"), Pass());

            Assert.That(requirement.Evaluate().IsSatisfied, Is.True);
        }

        [Test]
        public void Any_WhenAllFail_ReportsEveryFailure()
        {
            var result = Requirement.Any(Fail("a"), Fail("b")).Evaluate();

            Assert.That(result.IsSatisfied, Is.False);
            Assert.That(result.Failures.Select(failure => failure.Code), Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Not_InvertsTheInnerRequirement()
        {
            Assert.That(Requirement.Not(Fail("a"), "inverted").Evaluate().IsSatisfied, Is.True);

            var result = Requirement.Not(Pass(), "inverted").Evaluate();

            Assert.That(result.IsSatisfied, Is.False);
            Assert.That(result.FailureCode, Is.EqualTo("inverted"));
        }

        [Test]
        public void FailureResult_CarriesPresentationArguments()
        {
            var requirement = Requirement.FromPredicate(
                () => false,
                "locked",
                new Dictionary<string, object> { ["required"] = "Moon", ["amount"] = 12 });

            var result = requirement.Evaluate();

            Assert.That(result.FailureArguments["required"], Is.EqualTo("Moon"));
            Assert.That(result.FailureArguments["amount"], Is.EqualTo(12));
            Assert.That(result.HasFailure("locked"), Is.True);
            Assert.That(result.HasFailure("other"), Is.False);
        }

        [Test]
        public void ConditionRequirement_WrapsAPlainCondition()
        {
            var condition = new ToggleCondition { Value = true };
            var requirement = Requirement.FromCondition(condition, "off");

            Assert.That(requirement.Evaluate().IsSatisfied, Is.True);

            condition.Value = false;

            Assert.That(requirement.Evaluate().FailureCode, Is.EqualTo("off"));
        }

        private static IRequirement Pass() => Requirement.Always;

        private static IRequirement Fail(string code) => Requirement.FromPredicate(() => false, code);

        private sealed class ToggleCondition : ICondition
        {
            public bool Value { get; set; }

            public bool IsSatisfied() => Value;
        }
    }
}
