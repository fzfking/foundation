using System;
using UnityTime = UnityEngine.Time;

namespace Foundation.Timing
{
    public interface ITimeController
    {
        float SimulationScale { get; }
        float AnimationScale { get; }
        void SetSimulationScale(float scale);
        void SetAnimationScale(float scale);
    }

    public sealed class UnityTimeController : ITimeController
    {
        public float SimulationScale { get; private set; } = 1f;
        public float AnimationScale { get; private set; } = 1f;

        public void SetSimulationScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            SimulationScale = scale;
        }

        public void SetAnimationScale(float scale)
        {
            if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f)
                throw new ArgumentOutOfRangeException(nameof(scale));
            AnimationScale = scale;
            UnityTime.timeScale = scale;
        }
    }
}
