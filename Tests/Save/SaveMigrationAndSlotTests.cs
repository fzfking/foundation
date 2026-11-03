using System;
using System.Collections.Generic;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.Tests.Save
{
    public sealed class SaveMigrationAndSlotTests
    {
        private sealed class TestSerializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => BitConverter.GetBytes((int)(object)value);
            public T Deserialize<T>(byte[] bytes) => (T)(object)BitConverter.ToInt32(bytes, 0);
            public object Deserialize(Type type, byte[] bytes) => BitConverter.ToInt32(bytes, 0);
        }

        private sealed class TestModule : FeatureSaveModule<int>
        {
            public int Value;
            public override string SegmentId => "value";
            public override int Capture() => Value;
            public override void Restore(int snapshot) => Value = snapshot;
        }

        private sealed class RenamingMigration : ISaveMigration
        {
            public int FromVersion => 1;
            public int ToVersion => 2;

            public SaveData Migrate(SaveData data)
            {
                var segments = new Dictionary<string, byte[]>(data.Segments);
                if (segments.Remove("old-value", out var bytes))
                    segments["value"] = bytes;

                return new SaveData(ToVersion, segments, data.SavedAtUtcUnixMilliseconds);
            }
        }

        [Test]
        public void Load_OlderSave_AppliesRegisteredMigration()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            var oldData = new SaveData(1, new Dictionary<string, byte[]> { ["old-value"] = serializer.Serialize(42) });
            repository.SaveAsync(oldData).GetAwaiter().GetResult();

            var module = new TestModule();
            var orchestrator = new SaveOrchestrator(
                new IFeatureSaveModule[] { module }, repository, serializer,
                currentVersion: 2, migrations: new ISaveMigration[] { new RenamingMigration() });

            var loaded = orchestrator.LoadAsync().GetAwaiter().GetResult();

            Assert.That(loaded, Is.True);
            Assert.That(module.Value, Is.EqualTo(42));
        }

        [Test]
        public void Load_OlderSave_WithNoRegisteredMigration_ThrowsClearly()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]>())).GetAwaiter().GetResult();

            var orchestrator = new SaveOrchestrator(
                new IFeatureSaveModule[] { new TestModule() }, repository, serializer, currentVersion: 2);

            var exception = Assert.Throws<InvalidOperationException>(
                () => orchestrator.LoadAsync().GetAwaiter().GetResult());
            Assert.That(exception.Message, Does.Contain("No save migration registered"));
        }

        [Test]
        public void Load_NewerSaveThanSupported_ThrowsClearly()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            repository.SaveAsync(new SaveData(5, new Dictionary<string, byte[]>())).GetAwaiter().GetResult();

            var orchestrator = new SaveOrchestrator(
                new IFeatureSaveModule[] { new TestModule() }, repository, serializer, currentVersion: 2);

            var exception = Assert.Throws<InvalidOperationException>(
                () => orchestrator.LoadAsync().GetAwaiter().GetResult());
            Assert.That(exception.Message, Does.Contain("newer than the supported version"));
        }

        [Test]
        public void Constructor_DuplicateMigrationFromVersion_Throws()
        {
            var duplicate = new ISaveMigration[]
            {
                new RenamingMigration(),
                new RenamingMigration()
            };

            Assert.Throws<InvalidOperationException>(() => new SaveOrchestrator(
                new IFeatureSaveModule[] { new TestModule() },
                new InMemorySaveRepository(),
                new TestSerializer(),
                currentVersion: 2,
                migrations: duplicate));
        }

        [Test]
        public void InMemoryRepository_DefaultSlot_IsIndependentFromNamedSlots()
        {
            var repository = new InMemorySaveRepository();
            var defaultData = new SaveData(1, new Dictionary<string, byte[]> { ["a"] = new byte[] { 1 } });
            var namedData = new SaveData(1, new Dictionary<string, byte[]> { ["a"] = new byte[] { 2 } });

            repository.SaveAsync(defaultData).GetAwaiter().GetResult();
            repository.SaveAsync(namedData, slotId: "slot-2").GetAwaiter().GetResult();

            var loadedDefault = repository.LoadAsync().GetAwaiter().GetResult();
            var loadedNamed = repository.LoadAsync(slotId: "slot-2").GetAwaiter().GetResult();

            Assert.That(loadedDefault.Segments["a"][0], Is.EqualTo(1));
            Assert.That(loadedNamed.Segments["a"][0], Is.EqualTo(2));
        }

        [Test]
        public void InMemoryRepository_UnknownSlot_LoadsNull()
        {
            var repository = new InMemorySaveRepository();

            var loaded = repository.LoadAsync(slotId: "never-saved").GetAwaiter().GetResult();

            Assert.That(loaded, Is.Null);
        }
    }
}
