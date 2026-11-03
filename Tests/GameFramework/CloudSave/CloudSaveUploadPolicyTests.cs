#if FOUNDATION_GOOGLE_PLAY_GAMES
using System;
using System.Collections.Generic;
using System.IO;
using Foundation.GameFramework.CloudSave;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.GameFramework.Tests.CloudSave
{
    public sealed class CloudSaveUploadPolicyTests
    {
        private const long MinuteMilliseconds = 60_000;

        [Test]
        public void FirstUpload_IsAllowed_AtAnyTime()
        {
            var policy = CreatePolicy();

            Assert.That(policy.ShouldUpload(CreateSaveData(("economy", new byte[] { 1 })), MinuteMilliseconds), Is.True);
        }

        [Test]
        public void UnchangedContent_IsSkipped_EvenLongAfterThePreviousUpload()
        {
            var policy = CreatePolicy();
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), MinuteMilliseconds);

            // A fresh SaveData with the same segments but a new timestamp must still be
            // recognized as unchanged - otherwise every lifecycle checkpoint would re-upload.
            var unchanged = CreateSaveData(("economy", new byte[] { 1 }));
            Assert.That(policy.ShouldUpload(unchanged, MinuteMilliseconds * 1_000), Is.False);
        }

        [Test]
        public void ChangedContent_BeforeMinInterval_IsSkipped()
        {
            var policy = CreatePolicy();
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), 0);

            var changed = CreateSaveData(("economy", new byte[] { 2 }));
            Assert.That(policy.ShouldUpload(changed, MinuteMilliseconds - 1), Is.False);
        }

        [Test]
        public void ChangedContent_ExactlyAtMinInterval_IsAllowed()
        {
            var policy = CreatePolicy();
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), 0);

            var changed = CreateSaveData(("economy", new byte[] { 2 }));
            Assert.That(policy.ShouldUpload(changed, MinuteMilliseconds), Is.True);
        }

        [Test]
        public void AddedSegment_CountsAsChange()
        {
            var policy = CreatePolicy();
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), 0);

            var changed = CreateSaveData(
                ("economy", new byte[] { 1 }),
                ("prestige", new byte[] { 7 }));
            Assert.That(policy.ShouldUpload(changed, MinuteMilliseconds), Is.True);
        }

        [Test]
        public void SegmentValueTurnedNull_CountsAsChange()
        {
            var policy = CreatePolicy();
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), 0);

            var changed = CreateSaveData(("economy", null));
            Assert.That(policy.ShouldUpload(changed, MinuteMilliseconds), Is.True);
        }

        [Test]
        public void NullData_IsNeverUploaded()
        {
            var policy = CreatePolicy();

            Assert.That(policy.ShouldUpload(null, MinuteMilliseconds), Is.False);
        }

        [Test]
        public void ZeroMinInterval_UploadsEveryContentChange()
        {
            var policy = new CloudSaveUploadPolicy(TimeSpan.Zero);
            policy.OnUploaded(CreateSaveData(("economy", new byte[] { 1 })), 0);

            var changed = CreateSaveData(("economy", new byte[] { 2 }));
            Assert.That(policy.ShouldUpload(changed, 0), Is.True);
            policy.OnUploaded(changed, 0);
            Assert.That(policy.ShouldUpload(CreateSaveData(("economy", new byte[] { 2 })), 0), Is.False);
        }

        [Test]
        public void Slots_HaveIndependentContentAndUploadIntervals()
        {
            var policy = CreatePolicy();
            var data = CreateSaveData(("wallet", new byte[] { 1 }));
            policy.OnUploaded(data, MinuteMilliseconds, "A");

            Assert.That(policy.ShouldUpload(data, MinuteMilliseconds, "B"), Is.True);
            policy.OnUploaded(data, MinuteMilliseconds, "B");
            Assert.That(policy.ShouldUpload(data, MinuteMilliseconds * 2, "B"), Is.False);
            Assert.That(policy.ShouldUpload(CreateSaveData(("wallet", new byte[] { 2 })), MinuteMilliseconds * 2, "A"), Is.True);
        }

        [Test]
        public void VersionChange_IsUploadedEvenWhenSegmentsAreUnchanged()
        {
            var policy = CreatePolicy();
            var old = CreateSaveData(("wallet", new byte[] { 1 }));
            policy.OnUploaded(old, 0);
            Assert.That(policy.ShouldUpload(new SaveData(2, old.Segments), MinuteMilliseconds), Is.True);
        }

        [Test]
        public void UploadHistory_IsNotChangedByMutatingTheCallersSnapshot()
        {
            var policy = CreatePolicy();
            var data = CreateSaveData(("wallet", new byte[] { 1 }));
            policy.OnUploaded(data, 0);
            data.Segments["wallet"][0] = 2;
            Assert.That(policy.ShouldUpload(data, MinuteMilliseconds), Is.True);
        }

        [Test]
        public void CloudBackendInEditor_ReportsSkippedInsteadOfPersistence()
        {
            var signIn = new GooglePlayGamesSignIn(new Preference(), TimeSpan.FromSeconds(1));
            var backend = new GooglePlayGamesCloudSaveBackend("test", signIn, CreatePolicy());
            var data = CreateSaveData(("wallet", new byte[] { 1 }));

            Assert.That(backend.SaveWithResultAsync(data).GetAwaiter().GetResult(), Is.EqualTo(SaveWriteResult.Skipped));
            Assert.Throws<IOException>(() => backend.SaveAsync(data).GetAwaiter().GetResult());
        }

        private sealed class Preference : ICloudSavePreference
        {
            public bool IsEnabled => true;
            public void SetEnabled(bool enabled) { }
        }

        private static CloudSaveUploadPolicy CreatePolicy()
            => new CloudSaveUploadPolicy(TimeSpan.FromMilliseconds(MinuteMilliseconds));

        private static SaveData CreateSaveData(params (string Id, byte[] Bytes)[] segments)
        {
            var dictionary = new Dictionary<string, byte[]>(segments.Length, StringComparer.Ordinal);
            foreach (var (id, bytes) in segments)
                dictionary.Add(id, bytes);

            return new SaveData(1, dictionary, 1234567890);
        }
    }
}
#endif
