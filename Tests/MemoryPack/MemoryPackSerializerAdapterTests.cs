using System;
using Foundation.Infrastructure.MemoryPack;
using Foundation.Localization;
using Foundation.Tests.Serialization;
using Foundation.Tutorial;
using MemoryPack;
using NUnit.Framework;

namespace Foundation.Tests.MemoryPack
{
    public sealed class MemoryPackSerializerAdapterTests
    {
        private readonly MemoryPackSerializerAdapter _serializer = new();

        [Test]
        public void TutorialSnapshot_SurvivesTheAdapter_WithEveryChapterField()
        {
            var snapshot = new TutorialSnapshot
            {
                Chapters = new[]
                {
                    new TutorialChapterSnapshot
                    {
                        ChapterId = "intro",
                        Status = 2,
                        CurrentStepId = "tap-button",
                        IsPending = true,
                        CompletedStepIds = new[] { "welcome" },
                    },
                },
            };

            var restored = _serializer.Deserialize<TutorialSnapshot>(_serializer.Serialize(snapshot));

            Assert.That(restored.Chapters, Has.Length.EqualTo(1));
            Assert.That(restored.Chapters[0].ChapterId, Is.EqualTo("intro"));
            Assert.That(restored.Chapters[0].Status, Is.EqualTo(2));
            Assert.That(restored.Chapters[0].CurrentStepId, Is.EqualTo("tap-button"));
            Assert.That(restored.Chapters[0].IsPending, Is.True);
            Assert.That(restored.Chapters[0].CompletedStepIds, Is.EqualTo(new[] { "welcome" }));
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
            Assert.That(table.Count, Is.EqualTo(2));
        }

        [TestCaseSource(typeof(FoundationDataSamples), nameof(FoundationDataSamples.DataTypes))]
        public void EveryPublicField_SurvivesTheRoundTrip_AndTheHeaderCountsAllOfThem(Type type)
        {
            FoundationMemoryPackFormatters.Register();
            var original = FoundationDataSamples.Create(type);

            var bytes = MemoryPackSerializer.Serialize(type, original);
            var restored = MemoryPackSerializer.Deserialize(type, bytes);

            FoundationDataSamples.AssertSame(type, original, restored);
            // MemoryPack's object header is the member count; a field missing from the formatter would change it.
            Assert.That(bytes[0], Is.EqualTo(FoundationDataSamples.PublicFieldCount(type)));
        }

        [Test]
        public void TheNonGenericOverload_ReturnsTheRequestedType()
        {
            var bytes = _serializer.Serialize(new TutorialConfig { Id = "intro", Priority = 3, StepIds = new[] { "a", "b" } });

            var restored = _serializer.Deserialize(typeof(TutorialConfig), bytes);

            Assert.That(restored, Is.TypeOf<TutorialConfig>());
            Assert.That(((TutorialConfig)restored).StepIds, Is.EqualTo(new[] { "a", "b" }));
        }
    }
}
