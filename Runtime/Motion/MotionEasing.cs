using System;
using UnityEngine;

namespace Foundation.Motion
{
    /// <summary>Evaluates <see cref="MotionEase"/> curves: maps linear progress 0..1 to eased progress.</summary>
    public static class MotionEasing
    {
        private const float C1 = 1.70158f;
        private const float C2 = C1 * 1.525f;
        private const float C3 = C1 + 1f;
        private const float C4 = 2f * Mathf.PI / 3f;
        private const float C5 = 2f * Mathf.PI / 4.5f;

        public static float Evaluate(MotionEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case MotionEase.Linear: return t;
                case MotionEase.InSine: return 1f - Mathf.Cos(t * Mathf.PI / 2f);
                case MotionEase.OutSine: return Mathf.Sin(t * Mathf.PI / 2f);
                case MotionEase.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
                case MotionEase.InQuad: return t * t;
                case MotionEase.OutQuad: return 1f - (1f - t) * (1f - t);
                case MotionEase.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
                case MotionEase.InCubic: return t * t * t;
                case MotionEase.OutCubic: return 1f - Mathf.Pow(1f - t, 3f);
                case MotionEase.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case MotionEase.InQuart: return t * t * t * t;
                case MotionEase.OutQuart: return 1f - Mathf.Pow(1f - t, 4f);
                case MotionEase.InOutQuart: return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;
                case MotionEase.InQuint: return t * t * t * t * t;
                case MotionEase.OutQuint: return 1f - Mathf.Pow(1f - t, 5f);
                case MotionEase.InOutQuint: return t < 0.5f ? 16f * t * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 5f) / 2f;
                case MotionEase.InExpo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case MotionEase.OutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case MotionEase.InOutExpo:
                    if (t <= 0f)
                        return 0f;
                    if (t >= 1f)
                        return 1f;
                    return t < 0.5f ? Mathf.Pow(2f, 20f * t - 10f) / 2f : (2f - Mathf.Pow(2f, -20f * t + 10f)) / 2f;
                case MotionEase.InCirc: return 1f - Mathf.Sqrt(1f - t * t);
                case MotionEase.OutCirc: return Mathf.Sqrt(1f - (t - 1f) * (t - 1f));
                case MotionEase.InOutCirc:
                    return t < 0.5f
                        ? (1f - Mathf.Sqrt(1f - 4f * t * t)) / 2f
                        : (Mathf.Sqrt(1f - Mathf.Pow(-2f * t + 2f, 2f)) + 1f) / 2f;
                case MotionEase.InBack: return C3 * t * t * t - C1 * t * t;
                case MotionEase.OutBack: return 1f + C3 * Mathf.Pow(t - 1f, 3f) + C1 * Mathf.Pow(t - 1f, 2f);
                case MotionEase.InOutBack:
                    return t < 0.5f
                        ? Mathf.Pow(2f * t, 2f) * ((C2 + 1f) * 2f * t - C2) / 2f
                        : (Mathf.Pow(2f * t - 2f, 2f) * ((C2 + 1f) * (t * 2f - 2f) + C2) + 2f) / 2f;
                case MotionEase.InElastic:
                    if (t <= 0f)
                        return 0f;
                    if (t >= 1f)
                        return 1f;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * C4);
                case MotionEase.OutElastic:
                    if (t <= 0f)
                        return 0f;
                    if (t >= 1f)
                        return 1f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * C4) + 1f;
                case MotionEase.InOutElastic:
                    if (t <= 0f)
                        return 0f;
                    if (t >= 1f)
                        return 1f;
                    return t < 0.5f
                        ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * C5)) / 2f
                        : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * C5) / 2f + 1f;
                case MotionEase.InBounce: return 1f - OutBounce(1f - t);
                case MotionEase.OutBounce: return OutBounce(t);
                case MotionEase.InOutBounce:
                    return t < 0.5f ? (1f - OutBounce(1f - 2f * t)) / 2f : (1f + OutBounce(2f * t - 1f)) / 2f;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ease), ease, null);
            }
        }

        private static float OutBounce(float t)
        {
            const float n1 = 7.5625f;
            const float d1 = 2.75f;
            if (t < 1f / d1)
                return n1 * t * t;
            if (t < 2f / d1)
                return n1 * (t -= 1.5f / d1) * t + 0.75f;
            if (t < 2.5f / d1)
                return n1 * (t -= 2.25f / d1) * t + 0.9375f;
            return n1 * (t -= 2.625f / d1) * t + 0.984375f;
        }
    }
}
