using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Save;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace Foundation.Tests.Save
{
    public sealed class SaveLoadFailureTests
    {
        private static readonly SaveData Stored = new(1, new Dictionary<string, byte[]> { ["planet"] = new byte[] { 7 } });

        [Test]
        public void CompositeLoad_WhenOneBackendFailsAndTheOtherHasNoSave_Throws()
        {
            var composite = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new ScriptedRepository(() => throw new InvalidOperationException("unreadable")),
                new ScriptedRepository(() => null),
            });

            // Null here would read as "no save yet", and the first save would then overwrite the unreadable one.
            Assert.Throws<AggregateException>(() => composite.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void CompositeLoad_WhenOneBackendFailsButAnotherHasTheSave_ReturnsThatSave()
        {
            var composite = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new ScriptedRepository(() => throw new InvalidOperationException("offline")),
                new ScriptedRepository(() => Stored),
            });

            Assert.That(composite.LoadAsync().GetAwaiter().GetResult(), Is.SameAs(Stored));
        }

        [Test]
        public void CompositeLoad_WhenNoBackendFailsAndNoneHasASave_ReturnsNull()
        {
            var composite = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new ScriptedRepository(() => null),
                new ScriptedRepository(() => null),
            });

            Assert.That(composite.LoadAsync().GetAwaiter().GetResult(), Is.Null);
        }

        [Test]
        public void CompositeLoad_WhenEveryBackendFails_Throws()
        {
            var composite = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new ScriptedRepository(() => throw new InvalidOperationException("first")),
                new ScriptedRepository(() => throw new InvalidOperationException("second")),
            });

            Assert.Throws<AggregateException>(() => composite.LoadAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void Orchestrator_AfterAFailedLoad_RefusesToSave_AndNothingIsWritten()
        {
            var repository = new ScriptedRepository(() => throw new InvalidOperationException("unreadable"));
            var save = CreateOrchestrator(repository);

            Assert.Throws<InvalidOperationException>(() => save.LoadAsync().GetAwaiter().GetResult());
            Assert.Throws<InvalidOperationException>(() => save.SaveAsync().GetAwaiter().GetResult());
            Assert.That(repository.SaveCount, Is.Zero);
        }

        [Test]
        public void Orchestrator_AfterALoadThatFoundNothing_SavesAsANewGame()
        {
            var repository = new ScriptedRepository(() => null);
            var save = CreateOrchestrator(repository);

            Assert.That(save.LoadAsync().GetAwaiter().GetResult(), Is.False);
            save.SaveAsync().GetAwaiter().GetResult();

            Assert.That(repository.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void Orchestrator_RefusedSave_IsAllowedOnceALoadSucceeds()
        {
            var attempts = 0;
            var repository = new ScriptedRepository(() =>
            {
                if (attempts++ == 0)
                    throw new InvalidOperationException("unreadable");
                return null;
            });
            var save = CreateOrchestrator(repository);
            Assert.Throws<InvalidOperationException>(() => save.LoadAsync().GetAwaiter().GetResult());

            save.LoadAsync().GetAwaiter().GetResult();
            save.SaveAsync().GetAwaiter().GetResult();

            Assert.That(repository.SaveCount, Is.EqualTo(1));
        }

        private static SaveOrchestrator CreateOrchestrator(ISaveRepository repository)
            => new(Array.Empty<IFeatureSaveModule>(), repository, new UnusedSerializer());

        private sealed class ScriptedRepository : ISaveRepositoryBackend
        {
            private readonly Func<SaveData> _load;

            public ScriptedRepository(Func<SaveData> load) => _load = load;

            public int SaveCount { get; private set; }

            public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
                => UniTask.FromResult(_load());

            public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
            {
                SaveCount++;
                return UniTask.CompletedTask;
            }
        }

        private sealed class UnusedSerializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => throw new NotSupportedException("No module serializes in these tests.");

            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException("No module deserializes in these tests.");

            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException("No module deserializes in these tests.");
        }
    }
}
