using System;

namespace Foundation.ContentMirroring
{
    public interface IContentMirrorStatisticsStore
    {
        bool TryGetBytesPerSecond(string mirrorId, DateTimeOffset now, TimeSpan maximumAge, out double bytesPerSecond);
        void SaveBytesPerSecond(string mirrorId, double bytesPerSecond, DateTimeOffset measuredAt);
        void Remove(string mirrorId);
    }
}
