using System.Diagnostics;

namespace Foundation.Pooling
{
    /// <summary>Monotonic seconds. Injectable so budgeting and trimming are deterministic in tests.</summary>
    public interface IPoolClock
    {
        double Now { get; }
    }

    public sealed class StopwatchPoolClock : IPoolClock
    {
        public static readonly StopwatchPoolClock Instance = new();
        public double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    }
}
