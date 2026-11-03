using System;
using System.Collections.Generic;

namespace Foundation.Save
{
    public sealed class SaveData
    {
        public int Version { get; }

        public Dictionary<string, byte[]> Segments { get; }
        public long SavedAtUtcUnixMilliseconds { get; }

        public SaveData(int version, Dictionary<string, byte[]> segments, long? savedAtUtcUnixMilliseconds = null)
        {
            Version = version;
            Segments = segments ?? throw new ArgumentNullException(nameof(segments));
            SavedAtUtcUnixMilliseconds = savedAtUtcUnixMilliseconds ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
