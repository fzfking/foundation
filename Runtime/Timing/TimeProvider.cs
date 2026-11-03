using System;
using UnityTime = UnityEngine.Time;

namespace Foundation.Timing
{
    public interface ITimeProvider
    {
        DateTime UtcNow { get; }
        float Time { get; }

        float DeltaTime { get; }

        float UnscaledDeltaTime { get; }

        /// <summary>
        /// Monotonic time since the device/process booted: unlike <see cref="UtcNow"/>, changing the system
        /// clock cannot move it. The default falls back to
        /// <see cref="UnityTime.realtimeSinceStartupAsDouble"/> (monotonic everywhere, but
        /// process-relative), so existing implementers keep compiling.
        /// </summary>
        TimeSpan ElapsedSinceBoot => TimeSpan.FromSeconds(UnityTime.realtimeSinceStartupAsDouble);
    }

    public class SystemTimeProvider : ITimeProvider
    {
        public DateTime UtcNow => DateTime.UtcNow;
        public float Time => UnityTime.time;
        public float DeltaTime => UnityTime.deltaTime;
        public float UnscaledDeltaTime => UnityTime.unscaledDeltaTime;

        public TimeSpan ElapsedSinceBoot
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using var clock = new UnityEngine.AndroidJavaClass("android.os.SystemClock");
                return TimeSpan.FromMilliseconds(clock.CallStatic<long>("elapsedRealtime"));
#else
                return TimeSpan.FromSeconds(UnityTime.realtimeSinceStartupAsDouble);
#endif
            }
        }
    }
}
