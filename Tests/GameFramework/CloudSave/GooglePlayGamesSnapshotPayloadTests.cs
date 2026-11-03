#if FOUNDATION_GOOGLE_PLAY_GAMES
using System;
using System.Collections.Generic;
using System.IO;
using Foundation.GameFramework.CloudSave;
using Foundation.Save;
using NUnit.Framework;

namespace Foundation.GameFramework.Tests.CloudSave
{
    public sealed class GooglePlayGamesSnapshotPayloadTests
    {
        [Test]
        public void EncodeDecode_RoundTripsVersionTimestampAndSegments()
        {
            var data = CreateSaveData(
                ("economy", new byte[] { 1, 2, 3 }),
                ("production", Array.Empty<byte>()),
                ("prestige", null));
            data = new SaveData(data.Version, data.Segments, 9876543210);

            var restored = GooglePlayGamesSnapshotPayload.Decode(GooglePlayGamesSnapshotPayload.Encode(data));

            Assert.That(restored.Version, Is.EqualTo(data.Version));
            Assert.That(restored.SavedAtUtcUnixMilliseconds, Is.EqualTo(9876543210));
            Assert.That(restored.Segments.Keys, Is.EquivalentTo(new[] { "economy", "production", "prestige" }));
            Assert.That(restored.Segments["economy"], Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(restored.Segments["production"], Is.Empty);
            Assert.That(restored.Segments["prestige"], Is.Null);
        }

        [Test]
        public void Decode_RejectsTruncatedPayload()
        {
            var payload = GooglePlayGamesSnapshotPayload.Encode(
                CreateSaveData(("economy", new byte[] { 1, 2, 3 })));

            var truncated = new byte[payload.Length - 3];
            Array.Copy(payload, truncated, truncated.Length);

            Assert.That(() => GooglePlayGamesSnapshotPayload.Decode(truncated), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsEmptyPayload()
        {
            Assert.That(() => GooglePlayGamesSnapshotPayload.Decode(Array.Empty<byte>()), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsUnknownFormatVersion()
        {
            var payload = new byte[] { 99, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

            Assert.That(() => GooglePlayGamesSnapshotPayload.Decode(payload), Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsSegmentLengthBeyondPayload()
        {
            // Valid header (formatVersion 1), then a segment declaring a huge length.
            var payload = new List<byte>
            {
                1, 0, 0, 0, // formatVersion
                0, 0, 0, 0, 0, 0, 0, 0, // savedAt
                1, 0, 0, 0, // dataVersion
                1, 0, 0, 0, // segmentCount
            };
            payload.Add(1); // string length prefix = 1
            payload.Add((byte)'a');
            payload.AddRange(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }); // length = int.MaxValue

            Assert.That(
                () => GooglePlayGamesSnapshotPayload.Decode(payload.ToArray()),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsSegmentCountThatCannotFitInPayload()
        {
            var payload = CreatePayload(int.MaxValue);

            Assert.That(
                () => GooglePlayGamesSnapshotPayload.Decode(payload),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsSegmentIdLengthBeyondPayload()
        {
            var payload = CreatePayload(1, writer =>
                writer.Write(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x07 }));

            Assert.That(
                () => GooglePlayGamesSnapshotPayload.Decode(payload),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsNegativeSegmentLengthOtherThanNullSentinel()
        {
            var payload = CreatePayload(1, writer =>
            {
                writer.Write("economy");
                writer.Write(-2);
            });

            Assert.That(
                () => GooglePlayGamesSnapshotPayload.Decode(payload),
                Throws.TypeOf<InvalidDataException>());
        }

        [Test]
        public void Decode_RejectsDuplicateSegmentIdsAsInvalidPayload()
        {
            var payload = CreatePayload(2, writer =>
            {
                writer.Write("economy");
                writer.Write(0);
                writer.Write("economy");
                writer.Write(0);
            });

            Assert.That(
                () => GooglePlayGamesSnapshotPayload.Decode(payload),
                Throws.TypeOf<InvalidDataException>());
        }

        private static byte[] CreatePayload(int segmentCount, Action<BinaryWriter> writeSegments = null)
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(1);
                writer.Write(0L);
                writer.Write(1);
                writer.Write(segmentCount);
                writeSegments?.Invoke(writer);
                writer.Flush();
            }

            return stream.ToArray();
        }

        private static SaveData CreateSaveData(params (string Id, byte[] Bytes)[] segments)
        {
            var dictionary = new Dictionary<string, byte[]>(segments.Length, StringComparer.Ordinal);
            foreach (var (id, bytes) in segments)
                dictionary.Add(id, bytes);

            return new SaveData(1, dictionary);
        }
    }
}
#endif
