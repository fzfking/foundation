using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Foundation.Save;
using Foundation.Tests.Support;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine;

namespace Foundation.Tests.Save
{
    // A missing save is a new player or a reset, never a reason to bring a backup back.
    public sealed class LocalFileSaveRecoveryTests
    {
        private string _directory;
        private string _path;
        private LocalFileSaveRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "foundation-save-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            var location = new FixedLocation(_directory);
            _path = location.GetSaveFilePath(SaveSlot.Default);
            _repository = new(location);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, true);
        }

        [UnityTest]
        public IEnumerator AnUnreadableSave_LoadsThePreviousOne_AndTheNextSaveKeepsThatBackup() => UniTask.ToCoroutine(async () =>
        {
            await SaveAsync(1);
            await SaveAsync(2);
            File.WriteAllBytes(_path, new byte[] { 1, 2, 3 });
            using var log = new LogCapture();

            var recovered = await _repository.LoadAsync();

            Assert.That(log.Contains(LogType.Warning, "loaded its backup"), Is.True, "the recovery is reported");
            Assert.That(recovered.Segments["value"], Is.EqualTo(new byte[] { 1 }), "the previous save");
            Assert.That(File.Exists(LocalFileSaveRepository.CorruptPathOf(_path)), Is.True, "the broken copy is kept aside");

            Assert.That(File.Exists(_path), Is.True, "recovery survives a restart before the next autosave");
            var loadedAgain = await _repository.LoadAsync();
            Assert.That(loadedAgain.Segments["value"], Is.EqualTo(new byte[] { 1 }));

            await SaveAsync(3);
            Assert.That((await _repository.LoadAsync()).Segments["value"], Is.EqualTo(new byte[] { 3 }));
            File.WriteAllBytes(_path, new byte[] { 9 });
            Assert.That((await _repository.LoadAsync()).Segments["value"], Is.EqualTo(new byte[] { 1 }),
                "the good backup was not replaced by the broken file");
        });

        [UnityTest]
        public IEnumerator AMissingSave_IsANewPlayer_EvenWithABackupLeftBehind() => UniTask.ToCoroutine(async () =>
        {
            await SaveAsync(1);
            await SaveAsync(2);
            File.Delete(_path);

            Assert.That(await _repository.LoadAsync(), Is.Null);
        });

        [UnityTest]
        public IEnumerator ASaveAndBackupThatAreBothUnreadable_FailTheLoad() => UniTask.ToCoroutine(async () =>
        {
            await SaveAsync(1);
            await SaveAsync(2);
            File.WriteAllBytes(_path, new byte[] { 1 });
            File.WriteAllBytes(LocalFileSaveRepository.BackupPathOf(_path), new byte[] { 2 });

            var failed = false;
            try
            {
                await _repository.LoadAsync();
            }
            catch (InvalidDataException)
            {
                failed = true;
            }

            Assert.That(failed, Is.True, "an unknown save is never reported as no save");
        });

        [UnityTest]
        public IEnumerator ASegmentLongerThanTheFile_IsRejectedWithoutAllocatingIt() => UniTask.ToCoroutine(async () =>
        {
            using (var writer = new BinaryWriter(File.Create(_path)))
            {
                writer.Write(1);
                writer.Write(0L);
                writer.Write(1);
                writer.Write(1);
                writer.Write("value");
                writer.Write(int.MaxValue);
            }

            var failed = false;
            try
            {
                await _repository.LoadAsync();
            }
            catch (InvalidDataException)
            {
                failed = true;
            }

            Assert.That(failed, Is.True);
        });

        // The length prefix of a segment name is the one field BinaryReader rejects with FormatException or IOException
        // instead of running out of bytes, so a damaged one used to fail the load while a good backup sat beside it.
        [UnityTest]
        public IEnumerator ASegmentNameWithAnEndlessLengthPrefix_LoadsTheBackupInsteadOfFailing()
            => LoadsTheBackupForSegmentNamePrefix(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });

        [UnityTest]
        public IEnumerator ASegmentNameWithANegativeLength_LoadsTheBackupInsteadOfFailing()
            => LoadsTheBackupForSegmentNamePrefix(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F });

        private IEnumerator LoadsTheBackupForSegmentNamePrefix(byte[] damagedPrefix) => UniTask.ToCoroutine(async () =>
        {
            await SaveAsync(1);
            await SaveAsync(2);
            using (var stream = File.Create(_path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1);
                writer.Write(0L);
                writer.Write(1);
                writer.Write(1);
                writer.Write(damagedPrefix);
            }

            using var log = new LogCapture();
            var recovered = await _repository.LoadAsync();

            Assert.That(recovered.Segments["value"], Is.EqualTo(new byte[] { 1 }), "the previous save");
        });

        [UnityTest]
        public IEnumerator ANullSegment_ReadsBackAsNull() => UniTask.ToCoroutine(async () =>
        {
            await _repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]> { ["empty"] = null, ["value"] = new byte[] { 5 } }));

            var loaded = await _repository.LoadAsync();

            Assert.That(loaded.Segments.ContainsKey("empty"), Is.True);
            Assert.That(loaded.Segments["empty"], Is.Null);
            Assert.That(loaded.Segments["value"], Is.EqualTo(new byte[] { 5 }));
        });

        [Test]
        public void ASaveAfterLoadingOneFromAClockAhead_IsStampedLaterThanIt()
        {
            var repository = new InMemorySaveRepository();
            var ahead = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeMilliseconds();
            repository.SaveAsync(new SaveData(SaveOrchestrator.CurrentVersion, new Dictionary<string, byte[]>(), ahead))
                .GetAwaiter().GetResult();
            var orchestrator = new SaveOrchestrator(Array.Empty<IFeatureSaveModule>(), repository, new NoSerializer());

            orchestrator.LoadAsync().GetAwaiter().GetResult();
            orchestrator.SaveAsync().GetAwaiter().GetResult();

            // Backends keep the newest copy by this time: a lower stamp would let the stale copy win over what was just played.
            Assert.That(repository.LoadAsync().GetAwaiter().GetResult().SavedAtUtcUnixMilliseconds, Is.GreaterThan(ahead));
        }

        private UniTask SaveAsync(byte value)
            => _repository.SaveAsync(new SaveData(1, new Dictionary<string, byte[]> { ["value"] = new[] { value } }));

        private sealed class FixedLocation : ILocalSaveLocation
        {
            private readonly string _directory;

            public FixedLocation(string directory) => _directory = directory;

            public string GetSaveFilePath(string slotId) => Path.Combine(_directory, slotId + ".dat");
        }

        private sealed class NoSerializer : ISaveSerializer
        {
            public byte[] Serialize<T>(T value) => throw new NotSupportedException();
            public T Deserialize<T>(byte[] bytes) => throw new NotSupportedException();
            public object Deserialize(Type type, byte[] bytes) => throw new NotSupportedException();
        }
    }
}
