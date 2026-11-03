using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Foundation.Save;

namespace Foundation.GameFramework.CloudSave
{
    /// <summary>
    /// Binary envelope of a Play Games snapshot, built from the segmented <see cref="SaveData"/>; mirrors the
    /// local file layout of <c>LocalFileSaveRepository</c>. An implementation detail of the cloud backend.
    /// Layout: int formatVersion, long savedAtUtcUnixMilliseconds, int dataVersion, int segmentCount, then
    /// per segment: string id, int length (-1 for null), byte[] bytes.
    /// </summary>
    public static class GooglePlayGamesSnapshotPayload
    {
        private const int FormatVersion = 1;
        private static readonly Encoding PayloadEncoding = new UTF8Encoding(false, true);

        public static byte[] Encode(SaveData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(FormatVersion);
                writer.Write(data.SavedAtUtcUnixMilliseconds);
                writer.Write(data.Version);
                writer.Write(data.Segments.Count);
                foreach (var pair in data.Segments)
                {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value?.Length ?? -1);
                    if (pair.Value != null)
                        writer.Write(pair.Value);
                }

                writer.Flush();
            }

            return stream.ToArray();
        }

        public static SaveData Decode(byte[] payload)
        {
            if (payload == null)
                throw new ArgumentNullException(nameof(payload));

            try
            {
                using var stream = new MemoryStream(payload);
                using var reader = new BinaryReader(stream);
                if (reader.ReadInt32() != FormatVersion)
                    throw new InvalidDataException("Unsupported cloud save payload format.");

                var savedAtUtcUnixMilliseconds = reader.ReadInt64();
                var version = reader.ReadInt32();
                var segmentCount = reader.ReadInt32();
                if (segmentCount < 0)
                    throw new InvalidDataException("Cloud save segment count is invalid.");

                const int minimumSegmentRecordSize = 5; // one-byte string length prefix and four-byte data length
                if (segmentCount > (stream.Length - stream.Position) / minimumSegmentRecordSize)
                    throw new InvalidDataException("Cloud save segment count exceeds the payload size.");

                var segments = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                for (var i = 0; i < segmentCount; i++)
                {
                    var id = ReadSegmentId(reader, stream);
                    var length = reader.ReadInt32();
                    if (length == -1)
                    {
                        if (!segments.TryAdd(id, null))
                            throw new InvalidDataException($"Cloud save contains duplicate segment id '{id}'.");

                        continue;
                    }

                    if (length < -1)
                        throw new InvalidDataException("Cloud save segment length is invalid.");

                    if (length > stream.Length - stream.Position)
                        throw new InvalidDataException("Cloud save segment length is invalid.");

                    if (segments.ContainsKey(id))
                        throw new InvalidDataException($"Cloud save contains duplicate segment id '{id}'.");

                    segments.Add(id, reader.ReadBytes(length));
                }

                return new SaveData(version, segments, savedAtUtcUnixMilliseconds);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException("Cloud save payload is truncated.", exception);
            }
            catch (FormatException exception)
            {
                throw new InvalidDataException("Cloud save payload contains an invalid value.", exception);
            }
        }

        private static string ReadSegmentId(BinaryReader reader, Stream stream)
        {
            var byteLength = Read7BitEncodedInt(reader);
            if (byteLength > stream.Length - stream.Position)
                throw new InvalidDataException("Cloud save segment id length exceeds the payload size.");

            var bytes = reader.ReadBytes(byteLength);
            if (bytes.Length != byteLength)
                throw new InvalidDataException("Cloud save segment id is truncated.");

            try
            {
                return PayloadEncoding.GetString(bytes);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Cloud save segment id is not valid UTF-8.", exception);
            }
        }

        private static int Read7BitEncodedInt(BinaryReader reader)
        {
            uint result = 0;
            for (var index = 0; index < 5; index++)
            {
                var current = reader.ReadByte();
                if (index == 4 && (current & 0xF8) != 0)
                    throw new FormatException("Encoded string length exceeds the supported range.");

                result |= (uint)(current & 0x7F) << (index * 7);
                if ((current & 0x80) == 0)
                {
                    if (result > int.MaxValue)
                        throw new FormatException("Encoded string length exceeds the supported range.");

                    return (int)result;
                }
            }

            throw new FormatException("Encoded string length is invalid.");
        }
    }
}
