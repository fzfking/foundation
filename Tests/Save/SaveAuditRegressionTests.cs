using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.DI;
using Foundation.GameContexts;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.Tests.Save
{
    public sealed class SaveAuditRegressionTests
    {
        [Test]
        public void FailedLocalWriteAndSkippedCloudWrite_DoNotReportSuccess()
        {
            var repository = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new Backend { Failure = new IOException("Disk full.") },
                new Backend { Result = SaveWriteResult.Skipped },
            });

            Assert.Throws<AggregateException>(() => repository.SaveAsync(Data()).GetAwaiter().GetResult());
        }

        [Test]
        public void AllSkippedWrites_DoNotReportSuccess()
        {
            var repository = new CompositeSaveRepository(new[] { new Backend { Result = SaveWriteResult.Skipped } });
            Assert.Throws<AggregateException>(() => repository.SaveAsync(Data()).GetAwaiter().GetResult());
        }

        [Test]
        public void PersistedWrite_SucceedsDespiteAnotherBackendFailure()
        {
            var repository = new CompositeSaveRepository(new ISaveRepositoryBackend[]
            {
                new Backend(), new Backend { Failure = new IOException("Offline.") },
            });
            Assert.DoesNotThrow(() => repository.SaveAsync(Data()).GetAwaiter().GetResult());
        }

        [Test]
        public void LoadingAnotherSlot_DoesNotUnlockTheFailedSlot()
        {
            var backend = new Backend { FailedSlot = "broken" };
            var save = new SaveOrchestrator(Array.Empty<IFeatureSaveModule>(), backend, new Serializer());
            Assert.Throws<IOException>(() => save.LoadAsync(slotId: "broken").GetAwaiter().GetResult());
            save.LoadAsync(slotId: "new").GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() => save.SaveAsync(slotId: "broken").GetAwaiter().GetResult());
            Assert.DoesNotThrow(() => save.SaveAsync(slotId: "new").GetAwaiter().GetResult());
            backend.FailedSlot = null;
            save.LoadAsync(slotId: "broken").GetAwaiter().GetResult();
            Assert.DoesNotThrow(() => save.SaveAsync(slotId: "broken").GetAwaiter().GetResult());
        }

        [Test]
        public void MigrationAboveSupportedVersion_IsRejectedBeforeItRuns()
        {
            var backend = new Backend { Loaded = Data() };
            var migration = new OvershootingMigration();
            var save = new SaveOrchestrator(Array.Empty<IFeatureSaveModule>(), backend, new Serializer(), 2,
                new[] { migration });

            Assert.Throws<InvalidOperationException>(() => save.LoadAsync().GetAwaiter().GetResult());
            Assert.That(migration.Called, Is.False);
            Assert.Throws<InvalidOperationException>(() => save.SaveAsync().GetAwaiter().GetResult());
        }

        [Test]
        public void SaveHelper_UsesTheContextSlotVersionAndMigrations()
        {
            var backend = new Backend { Loaded = Data() };
            var migration = new ContextMigration();
            using var scope = MinimalServiceScope.Create(registry =>
            {
                registry.RegisterInstance(new GameContextSaveProfile("season", 2, new[] { migration }));
                registry.RegisterInstance<ISaveRepository>(backend);
                registry.RegisterInstance<ISaveSerializer>(new Serializer());
                registry.UseSaveOrchestrator();
            });
            var save = scope.Resolve<SaveOrchestrator>();

            Assert.That(save.LoadAsync().GetAwaiter().GetResult(), Is.True);
            save.SaveAsync().GetAwaiter().GetResult();

            Assert.That(backend.LastSlot, Is.EqualTo("season"));
            Assert.That(backend.Written.Version, Is.EqualTo(2));
            Assert.That(migration.Called, Is.True);
        }

        private static SaveData Data() => new(1, new Dictionary<string, byte[]>());

        private sealed class Backend : ISaveRepositoryBackendWithResult
        {
            public SaveWriteResult Result = SaveWriteResult.Persisted;
            public Exception Failure;
            public string FailedSlot;
            public string LastSlot;
            public SaveData Loaded;
            public SaveData Written;

            public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
            {
                LastSlot = slotId;
                if (slotId == FailedSlot)
                    throw new IOException("Unreadable slot.");
                return UniTask.FromResult(Loaded);
            }

            public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
            {
                Written = data;
                LastSlot = slotId;
                return UniTask.CompletedTask;
            }

            public UniTask<SaveWriteResult> SaveWithResultAsync(
                SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
                => Failure != null ? throw Failure : UniTask.FromResult(Result);
        }

        private sealed class Serializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => throw new NotSupportedException();
            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();
            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }

        private sealed class OvershootingMigration : ISaveMigration
        {
            public bool Called;
            public int FromVersion => 1;
            public int ToVersion => 3;
            public SaveData Migrate(SaveData data)
            {
                Called = true;
                return new SaveData(3, data.Segments);
            }
        }

        private sealed class ContextMigration : ISaveMigration
        {
            public bool Called;
            public int FromVersion => 1;
            public int ToVersion => 2;
            public SaveData Migrate(SaveData data)
            {
                Called = true;
                return new SaveData(2, data.Segments);
            }
        }
    }
}
