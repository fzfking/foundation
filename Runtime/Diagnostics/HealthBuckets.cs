namespace Foundation.Diagnostics
{
    /// <summary>
    /// Health numbers as a handful of named ranges. Every analytics provider counts distinct parameter values, so a raw frame
    /// rate or a byte count would multiply the event's cardinality; ranges keep it to a few values per parameter and read
    /// better in a dashboard anyway.
    /// </summary>
    public static class HealthBuckets
    {
        public static string Fps(double averageFps)
            => averageFps < 20d ? "lt20" : averageFps < 30d ? "20_30" : averageFps < 45d ? "30_45" : averageFps < 58d ? "45_58" : "58_plus";

        public static string SlowFrames(double percent)
            => percent < 0.5d ? "lt0_5" : percent < 2d ? "0_5_2" : percent < 5d ? "2_5" : percent < 10d ? "5_10" : "10_plus";

        public static string Hitches(int count)
            => count == 0 ? "0" : count <= 2 ? "1_2" : count <= 5 ? "3_5" : "6_plus";

        public static string Duration(double seconds)
            => seconds < 60d ? "lt1m" : seconds < 300d ? "1_5m" : seconds < 900d ? "5_15m" : seconds < 1800d ? "15_30m" : "30m_plus";

        public static string RamTier(int systemMemoryMegabytes)
            => systemMemoryMegabytes < 3000 ? "low" : systemMemoryMegabytes < 6000 ? "mid" : "high";

        /// <summary>Memory in steps of 256 MB, capped, so it is at most a dozen distinct values.</summary>
        public static int MemoryStepMegabytes(long bytes)
        {
            var megabytes = bytes / (1024L * 1024L);
            var step = (int)(megabytes / 256L) * 256;
            return step > 3072 ? 3072 : step;
        }
    }
}
