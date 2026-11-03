using System;
using Foundation.Infrastructure.MessagePack;
using Foundation.Localization;
using Foundation.Tests.Serialization;
using Foundation.Tutorial;
using Foundation.Tutorial.Flow;
using MessagePack;
using NUnit.Framework;

namespace Foundation.Tests.MessagePack
{
    public sealed class MessagePackSerializerAdapterTests
    {
        private readonly MessagePackSerializerAdapter _serializer = new();

        [TestCaseSource(typeof(FoundationDataSamples), nameof(FoundationDataSamples.DataTypes))]
        public void EveryPublicField_SurvivesTheRoundTrip_AndTheHeaderCountsAllOfThem(Type type)
        {
            var original = FoundationDataSamples.Create(type);

            var bytes = MessagePackSerializer.Serialize(type, original, MessagePackSerializerOptions.Standard.WithResolver(FoundationMessagePackResolver.Instance));
            var restored = _serializer.Deserialize(type, bytes);

            FoundationDataSamples.AssertSame(type, original, restored);
            // A fixarray header is 0x90 plus the element count; a field missing from the formatter would change it.
            Assert.That(bytes[0], Is.EqualTo(0x90 + FoundationDataSamples.PublicFieldCount(type)));
        }

        [Test]
        public void ARootArrayOfConfigs_RoundTrips_BecauseAConfigGroupIsExportedThatWay()
        {
            var scripts = new[]
            {
                new TutorialScriptConfig { Id = "a", Priority = 1, Steps = new[] { new TutorialStepScript { Id = "s1", IsCheckpoint = true } } },
                new TutorialScriptConfig { Id = "b", Priority = 2 },
            };

            var restored = _serializer.Deserialize<TutorialScriptConfig[]>(_serializer.Serialize(scripts));

            Assert.That(restored, Has.Length.EqualTo(2));
            Assert.That(restored[0].Steps[0].IsCheckpoint, Is.True);
            Assert.That(restored[1].Steps, Is.Null);
        }

        [Test]
        public void NullAndEmptyArrays_StayDistinct()
        {
            var config = new TutorialConfig { Id = "x", StepIds = Array.Empty<string>(), CheckpointStepIds = null };

            var restored = _serializer.Deserialize<TutorialConfig>(_serializer.Serialize(config));

            Assert.That(restored.StepIds, Is.Not.Null.And.Empty);
            Assert.That(restored.CheckpointStepIds, Is.Null);
        }

        [Test]
        public void ANewerWritersExtraFields_AreSkipped_AndAnOlderWritersMissingOnesStayDefault()
        {
            var options = MessagePackSerializerOptions.Standard;
            var newer = MessagePackSerializer.Serialize(new object[] { "id", "trigger", 4, new[] { "a" }, new[] { "b" }, "future", 99 }, options);
            var older = MessagePackSerializer.Serialize(new object[] { "id", "trigger" }, options);

            var fromNewer = _serializer.Deserialize<TutorialConfig>(newer);
            var fromOlder = _serializer.Deserialize<TutorialConfig>(older);

            Assert.That(fromNewer.Priority, Is.EqualTo(4));
            Assert.That(fromNewer.CheckpointStepIds, Is.EqualTo(new[] { "b" }));
            Assert.That(fromOlder.TriggerId, Is.EqualTo("trigger"));
            Assert.That(fromOlder.Priority, Is.EqualTo(0));
            Assert.That(fromOlder.StepIds, Is.Null);
        }

        [Test]
        public void LocalizationTableData_SurvivesTheAdapter_AndBuildsATable()
        {
            var data = new LocalizationTableData
            {
                Locale = "en",
                Keys = new[] { "ui.play", "ui.quit" },
                Values = new[] { "Play", "Quit" },
            };

            var restored = _serializer.Deserialize<LocalizationTableData>(_serializer.Serialize(data));
            var table = LocalizationTable.FromData(restored);

            Assert.That(table.TryGet(new LocalizationKey("ui.play"), out var template), Is.True);
            Assert.That(template, Is.EqualTo("Play"));
        }
    }
}
