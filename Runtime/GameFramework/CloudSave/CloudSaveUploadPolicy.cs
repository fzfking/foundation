using System;
using System.Collections.Generic;
using Foundation.Save;

namespace Foundation.GameFramework.CloudSave
{
    /// <summary>
    /// Traffic policy for cloud uploads: commit only when the save content differs from the last upload and
    /// the minimum interval has elapsed. Commits are expensive while local saves run on every checkpoint.
    /// History is per slot and includes the save version; timestamps change on every checkpoint and are not content.
    /// </summary>
    public sealed class CloudSaveUploadPolicy
    {
        private readonly double _minIntervalMilliseconds;
        private readonly Dictionary<string, Upload> _uploads = new(StringComparer.Ordinal);

        public CloudSaveUploadPolicy(TimeSpan minInterval)
        {
            if (minInterval < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(minInterval));

            _minIntervalMilliseconds = minInterval.TotalMilliseconds;
        }

        public bool ShouldUpload(SaveData data, long nowUtcUnixMilliseconds, string slotId = SaveSlot.Default)
        {
            if (data == null)
                return false;
            if (!_uploads.TryGetValue(slotId, out var last))
                return true;
            if (last.Version == data.Version && EqualsSegments(last.Segments, data.Segments))
                return false;

            return nowUtcUnixMilliseconds - last.AtUtcUnixMilliseconds >= _minIntervalMilliseconds;
        }

        public void OnUploaded(SaveData data, long nowUtcUnixMilliseconds, string slotId = SaveSlot.Default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var segments = new Dictionary<string, byte[]>(data.Segments.Count, StringComparer.Ordinal);
            foreach (var pair in data.Segments)
                segments.Add(pair.Key, (byte[])pair.Value?.Clone());
            _uploads[slotId] = new Upload(data.Version, segments, nowUtcUnixMilliseconds);
        }

        private static bool EqualsSegments(Dictionary<string, byte[]> left, Dictionary<string, byte[]> right)
        {
            if (left.Count != right.Count)
                return false;

            foreach (var pair in right)
            {
                if (!left.TryGetValue(pair.Key, out var lastBytes) || !EqualsBytes(lastBytes, pair.Value))
                    return false;
            }

            return true;
        }

        private sealed class Upload
        {
            public readonly int Version;
            public readonly Dictionary<string, byte[]> Segments;
            public readonly long AtUtcUnixMilliseconds;

            public Upload(int version, Dictionary<string, byte[]> segments, long atUtcUnixMilliseconds)
            {
                Version = version;
                Segments = segments;
                AtUtcUnixMilliseconds = atUtcUnixMilliseconds;
            }
        }

        private static bool EqualsBytes(byte[] left, byte[] right)
        {
            if (left == null || right == null)
                return left == right;
            if (left.Length != right.Length)
                return false;

            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                    return false;
            }

            return true;
        }
    }
}
