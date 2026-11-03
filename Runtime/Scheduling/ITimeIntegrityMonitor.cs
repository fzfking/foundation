using System;

namespace Foundation.Scheduling
{
    public interface ITimeIntegrityMonitor
    {
        void Observe(float unscaledDeltaTime, TimeSpan realElapsed);
    }
}
