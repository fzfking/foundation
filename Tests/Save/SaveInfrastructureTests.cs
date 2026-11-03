using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.Tests.Save
{
    public class SaveInfrastructureTests
    {
        [Test]
        public void SaveAndLoad_MultipleModules_UseSeparateSegments()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            var first = new TestModule("first", 10);
            var second = new TestModule("second", 20);
            var save = new SaveOrchestrator(new IFeatureSaveModule[] { first, second }, repository, serializer);

            save.SaveAsync().GetAwaiter().GetResult();
            var data = repository.LoadAsync().GetAwaiter().GetResult();
            first.Value = 0;
            second.Value = 0;
            new SaveOrchestrator(new IFeatureSaveModule[] { first, second }, repository, serializer)
                .LoadAsync().GetAwaiter().GetResult();

            Assert.That(data.Segments.Keys, Is.EquivalentTo(new[] { "first", "second" }));
            Assert.That(first.Value, Is.EqualTo(10));
            Assert.That(second.Value, Is.EqualTo(20));
        }

        [Test]
        public void Load_MissingSegment_LeavesModuleInitialState()
        {
            var repository = new InMemorySaveRepository();
            repository.SaveAsync(new SaveData(SaveOrchestrator.CurrentVersion,
                new Dictionary<string, byte[]> { ["other"] = new byte[] { 1 } })).GetAwaiter().GetResult();
            var module = new TestModule("planet", 3);

            new SaveOrchestrator(new IFeatureSaveModule[] { module }, repository, new TestSerializer())
                .LoadAsync().GetAwaiter().GetResult();

            Assert.That(module.Value, Is.EqualTo(3));
        }

        [Test]
        public void Constructor_DuplicateSegmentId_ThrowsClearly()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new SaveOrchestrator(
                new IFeatureSaveModule[] { new TestModule("planet", 1), new TestModule("planet", 2) },
                new InMemorySaveRepository(), new TestSerializer()));

            Assert.That(exception.Message, Does.Contain("Duplicate save segment id 'planet'"));
        }

        [Test]
        public void Save_FailedModule_DoesNotOverwriteExistingSave()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            new SaveOrchestrator(new IFeatureSaveModule[] { new TestModule("safe", 7) }, repository, serializer)
                .SaveAsync().GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => new SaveOrchestrator(
                    new IFeatureSaveModule[] { new ThrowingModule("broken") }, repository, serializer)
                .SaveAsync().GetAwaiter().GetResult());

            var data = repository.LoadAsync().GetAwaiter().GetResult();
            Assert.That(data.Segments.ContainsKey("safe"), Is.True);
            Assert.That(data.Segments.ContainsKey("broken"), Is.False);
        }

        [Test]
        public void Load_FailedModule_StopsBeforeRestoringFollowingModules()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            new SaveOrchestrator(
                    new IFeatureSaveModule[] { new TestModule("broken", 10), new TestModule("following", 20) },
                    repository,
                    serializer)
                .SaveAsync().GetAwaiter().GetResult();

            var following = new TestModule("following", 0);
            var load = new SaveOrchestrator(
                new IFeatureSaveModule[] { new ThrowingRestoreModule("broken"), following },
                repository,
                serializer);

            Assert.Throws<InvalidOperationException>(() => load.LoadAsync().GetAwaiter().GetResult());
            Assert.That(following.Value, Is.Zero);
        }

        [Test]
        public void Load_FailedRestore_RollsBackTheModuleRestoredBeforeIt_AndKeepsTheSlotBlocked()
        {
            var repository = new InMemorySaveRepository();
            var serializer = new TestSerializer();
            new SaveOrchestrator(
                    new IFeatureSaveModule[] { new TestModule("planet", 10), new TestModule("broken", 20) },
                    repository,
                    serializer)
                .SaveAsync().GetAwaiter().GetResult();

            var planet = new TestModule("planet", 1);
            var load = new SaveOrchestrator(
                new IFeatureSaveModule[] { planet, new ThrowingRestoreModule("broken") },
                repository,
                serializer);

            Assert.Throws<InvalidOperationException>(() => load.LoadAsync().GetAwaiter().GetResult());
            Assert.That(planet.Value, Is.EqualTo(1), "the planet was restored before the failure, so it is put back");
            Assert.Throws<InvalidOperationException>(() => load.SaveAsync().GetAwaiter().GetResult(),
                "the failed load still blocks saving over the unread save");
        }

        [Test]
        public void Repository_SaveLoadOverwriteAndCancellation_WorkAsExpected()
        {
            var repository = new InMemorySaveRepository();
            Assert.That(repository.LoadAsync().GetAwaiter().GetResult(), Is.Null);
            repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]> { ["a"] = new byte[] { 1 } }))
                .GetAwaiter().GetResult();
            repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]> { ["b"] = new byte[] { 2 } }))
                .GetAwaiter().GetResult();

            var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(
                () => repository.LoadAsync(cts.Token).GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(
                () => repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]>()), cts.Token)
                    .GetAwaiter().GetResult());
            Assert.That(repository.LoadAsync().GetAwaiter().GetResult().Segments.ContainsKey("b"), Is.True);
        }

        private sealed class TestModule : FeatureSaveModule<TestSnapshot>
        {
            public TestModule(string segmentId, int value)
            {
                SegmentId = segmentId;
                Value = value;
            }

            public override string SegmentId { get; }
            public int Value { get; set; }
            public override TestSnapshot Capture() => new(Value);
            public override void Restore(TestSnapshot snapshot) => Value = snapshot.Value;
        }

        private sealed class ThrowingModule : FeatureSaveModule<TestSnapshot>
        {
            public ThrowingModule(string segmentId) => SegmentId = segmentId;
            public override string SegmentId { get; }
            public override TestSnapshot Capture() => throw new InvalidOperationException("Expected failure.");
            public override void Restore(TestSnapshot snapshot) { }
        }

        private sealed class ThrowingRestoreModule : FeatureSaveModule<TestSnapshot>
        {
            public ThrowingRestoreModule(string segmentId) => SegmentId = segmentId;
            public override string SegmentId { get; }
            public override TestSnapshot Capture() => new(0);
            public override void Restore(TestSnapshot snapshot) => throw new InvalidOperationException("Expected restore failure.");
        }

        private sealed class TestSnapshot
        {
            public TestSnapshot(int value) => Value = value;
            public int Value { get; }
        }

        private sealed class TestSerializer : ISaveSerializer
        {
            private readonly Dictionary<byte, object> _values = new();
            private byte _nextId;

            public byte[] Serialize<T>(T value)
            {
                var id = ++_nextId;
                _values.Add(id, value);
                return new[] { id };
            }

            public T Deserialize<T>(byte[] bytes) => (T)_values[bytes[0]];
            public object Deserialize(Type type, byte[] bytes) => _values[bytes[0]];
        }
    }
}
