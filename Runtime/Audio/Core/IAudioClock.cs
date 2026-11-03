
using UnityTime = UnityEngine.Time;

namespace Foundation.Audio
{
    /// <summary>Monotonic seconds that keep running while the game is paused or slowed down.</summary>
    public interface IAudioClock
    {
        double Now { get; }
    }

    public sealed class UnscaledAudioClock : IAudioClock
    {
        public static readonly UnscaledAudioClock Instance = new();
        public double Now => UnityTime.unscaledTimeAsDouble;
    }
}
