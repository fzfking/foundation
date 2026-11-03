using System.Collections.Generic;
using System.Linq;
using Foundation.TypedIds.Editor;
using NUnit.Framework;

namespace Foundation.Tests.TypedIds
{
    public sealed class StableIdValidatorTests
    {
        [Test]
        public void Definitions_ReportDuplicateIds_NamingBothOwners()
        {
            var entries = new List<StableIdEntry>
            {
                new StableIdEntry("ui.click", "Click"),
                new StableIdEntry("ui.hover", "Hover"),
                new StableIdEntry("ui.click", "Click (copy)"),
            };

            var issues = StableIdValidator.ValidateDefinitions(entries);

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Kind, Is.EqualTo(StableIdIssueKind.Duplicate));
            Assert.That(issues[0].Message, Does.Contain("Click").And.Contain("Click (copy)"));
        }

        [Test]
        public void Definitions_ReportEmptyAndMalformedIds_AndStillCheckTheRest()
        {
            var entries = new List<StableIdEntry>
            {
                new StableIdEntry("", "Nameless"),
                new StableIdEntry("has space", "Spaced"),
                new StableIdEntry("fine", "Fine"),
                new StableIdEntry("fine", "Fine again"),
            };

            var issues = StableIdValidator.ValidateDefinitions(entries);

            Assert.That(issues.Select(issue => issue.Kind), Is.EqualTo(new[]
            {
                StableIdIssueKind.InvalidKey, StableIdIssueKind.InvalidKey, StableIdIssueKind.Duplicate,
            }));
        }

        [Test]
        public void References_ReportOnlyIdsWithoutADefinition_AndAllowEmptyOnes()
        {
            var entries = new List<StableIdEntry> { new StableIdEntry("known", "Known") };
            var references = new List<StableIdReference>
            {
                new StableIdReference("known", null, "Assets/A.asset", "id"),
                new StableIdReference("renamed.away", null, "Assets/B.asset", "events.Array.data[0].id"),
                new StableIdReference("", null, "Assets/C.asset", "optional"),
            };

            var issues = StableIdValidator.ValidateReferences(entries, references);

            Assert.That(issues, Has.Count.EqualTo(1));
            Assert.That(issues[0].Kind, Is.EqualTo(StableIdIssueKind.MissingDefinition));
            Assert.That(issues[0].Message, Does.Contain("Assets/B.asset").And.Contain("renamed.away"));
        }

        [Test]
        public void EntryWithoutDisplayName_FallsBackToTheRawId()
        {
            Assert.That(new StableIdEntry("night_hunt", null).DisplayName, Is.EqualTo("night_hunt"));
        }
    }
}
