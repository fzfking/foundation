using System;

namespace Foundation.Audio
{
    /// <summary>
    /// While anything plays on <see cref="Trigger"/>, <see cref="Target"/> is turned down to
    /// <see cref="TargetGain"/> (linear, 0.5 is about -6 dB) and restored afterwards. For example music under
    /// voice lines.
    /// </summary>
    public readonly struct AudioDuckingRule
    {
        public AudioChannelId Trigger { get; }
        public AudioChannelId Target { get; }
        public float TargetGain { get; }
        public float AttackSeconds { get; }
        public float ReleaseSeconds { get; }

        public AudioDuckingRule(
            AudioChannelId trigger,
            AudioChannelId target,
            float targetGain = 0.5f,
            float attackSeconds = 0.15f,
            float releaseSeconds = 0.5f)
        {
            if (trigger == target)
                throw new ArgumentException("A channel cannot duck itself.", nameof(target));

            Trigger = trigger;
            Target = target;
            TargetGain = Math.Max(0f, Math.Min(1f, targetGain));
            AttackSeconds = Math.Max(0f, attackSeconds);
            ReleaseSeconds = Math.Max(0f, releaseSeconds);
        }
    }
}
