using System.Collections.Generic;
using System.Linq;
using Foundation.Audio;
using Foundation.Audio.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Foundation.Tests.Audio
{
    public sealed class AudioEventValidatorTests
    {
        private readonly List<Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var item in _cleanup)
                Object.DestroyImmediate(item);

            _cleanup.Clear();
        }

        [Test]
        public void AWellFormedEvent_HasNoIssues()
        {
            var issues = Validate(Event("ui.click", "ui", clips: 3, selection: AudioSelectionMode.RandomNoImmediateRepeat), new[] { "ui" });

            Assert.That(issues, Is.Empty);
        }

        [Test]
        public void Duplicates_AcrossLibraries_AreErrors()
        {
            var issues = Validate(new[] { Event("a", "ui"), Event("a", "ui") }, new[] { "ui" });

            Assert.That(issues.Where(issue => issue.Message.Contains("Duplicate")).Count(), Is.EqualTo(1));
            Assert.That(issues[0].Severity, Is.EqualTo(AudioIssueSeverity.Error));
        }

        [Test]
        public void MissingClips_UnknownChannel_AndEmptyIds_AreErrors()
        {
            var noClips = Event("silent", "ui", clips: 0);
            var badChannel = Event("lost", "ghost");
            var noId = Event("", "ui");

            var issues = Validate(new[] { noClips, badChannel, noId }, new[] { "ui" });

            Assert.That(issues.Select(issue => issue.Message), Has.Some.Contain("No clips"));
            Assert.That(issues.Select(issue => issue.Message), Has.Some.Contain("Channel 'ghost' does not exist"));
            Assert.That(issues.Select(issue => issue.Message), Has.Some.Contain("Invalid id"));
            Assert.That(issues.All(issue => issue.Severity == AudioIssueSeverity.Error), Is.True);
        }

        [Test]
        public void WithoutAChannelCatalog_ChannelNamesAreNotJudged()
        {
            Assert.That(Validate(Event("lost", "defined.in.code"), null), Is.Empty);
        }

        [Test]
        public void UnreasonableRanges_AreReported_WithTheRightSeverity()
        {
            var inverted = Event("a", "ui", volume: new Vector2(0.9f, 0.5f), pitch: new Vector2(1.2f, 0.8f));
            var extreme = Event("b", "ui", volume: new Vector2(0.5f, 2f), pitch: new Vector2(0.1f, 5f), cooldown: 30f);

            var issues = Validate(new[] { inverted, extreme }, new[] { "ui" });

            var errors = issues.Where(issue => issue.Severity == AudioIssueSeverity.Error).Select(issue => issue.Message).ToList();
            var warnings = issues.Where(issue => issue.Severity == AudioIssueSeverity.Warning).Select(issue => issue.Message).ToList();
            Assert.That(errors, Has.Some.Contain("Volume minimum is above"));
            Assert.That(errors, Has.Some.Contain("Pitch minimum is above"));
            Assert.That(warnings, Has.Some.Contain("Volume outside 0..1"));
            Assert.That(warnings, Has.Some.Contain("Pitch outside"));
            Assert.That(warnings, Has.Some.Contain("cooldown"));
        }

        [Test]
        public void SelectionModeThatDoesNothing_IsAWarning()
        {
            var singleWithMany = Event("a", "ui", clips: 3, selection: AudioSelectionMode.Single);
            var randomWithOne = Event("b", "ui", clips: 1, selection: AudioSelectionMode.Random);

            var issues = Validate(new[] { singleWithMany, randomWithOne }, new[] { "ui" });

            Assert.That(issues, Has.Count.EqualTo(2));
            Assert.That(issues.All(issue => issue.Severity == AudioIssueSeverity.Warning), Is.True);
        }

        private List<AudioValidationIssue> Validate(AudioEventDefinition definition, string[] channels) => Validate(new[] { definition }, channels);

        private static List<AudioValidationIssue> Validate(AudioEventDefinition[] definitions, string[] channels)
            => AudioEventValidator.Validate(definitions, null, channels == null ? null : new HashSet<string>(channels));

        private AudioEventDefinition Event(
            string id,
            string channel,
            int clips = 1,
            AudioSelectionMode selection = AudioSelectionMode.Single,
            Vector2? volume = null,
            Vector2? pitch = null,
            float cooldown = 0f)
        {
            var entries = new AudioClipEntry[clips];
            for (var index = 0; index < clips; index++)
            {
                var clip = AudioClip.Create("c", 100, 1, 44100, false);
                _cleanup.Add(clip);
                entries[index] = new AudioClipEntry(clip);
            }

            var v = volume ?? Vector2.one;
            var p = pitch ?? Vector2.one;
            return new AudioEventDefinition(
                AudioEventId.From(id), AudioChannelId.From(channel), entries, selection: selection,
                volumeMin: v.x, volumeMax: v.y, pitchMin: p.x, pitchMax: p.y, cooldownSeconds: cooldown);
        }
    }
}
