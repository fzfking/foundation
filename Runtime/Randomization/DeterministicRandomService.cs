using System;
using System.Collections.Generic;

namespace Foundation.Randomization
{
    /// <summary>The whole state of a <see cref="DeterministicRandomService"/>: four words that a save can keep and restore.</summary>
    public readonly struct RandomState : IEquatable<RandomState>
    {
        public ulong S0 { get; }
        public ulong S1 { get; }
        public ulong S2 { get; }
        public ulong S3 { get; }

        /// <summary>The all-zero state the generator can never leave, so it is never a valid state.</summary>
        public bool IsValid => (S0 | S1 | S2 | S3) != 0;

        public RandomState(ulong s0, ulong s1, ulong s2, ulong s3)
        {
            S0 = s0;
            S1 = s1;
            S2 = s2;
            S3 = s3;
        }

        public bool Equals(RandomState other) => S0 == other.S0 && S1 == other.S1 && S2 == other.S2 && S3 == other.S3;
        public override bool Equals(object obj) => obj is RandomState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(S0, S1, S2, S3);
    }

    /// <summary>
    /// A random source whose sequence is a pure function of its seed: identical on every device and runtime,
    /// restorable from a save, and splittable into independent streams so adding draws to one system never
    /// shifts another's.
    /// </summary>
    public interface IDeterministicRandom : IRandomService
    {
        ulong Seed { get; }
        ulong NextUInt64();
        RandomState CaptureState();

        /// <summary>Continues exactly where the captured source was.</summary>
        void RestoreState(RandomState state);

        /// <summary>
        /// An independent stream derived from the seed and <paramref name="streamName"/> only, not from how
        /// many values were drawn, so a name always yields the same stream.
        /// </summary>
        IDeterministicRandom Fork(string streamName);
    }

    /// <summary>
    /// xoshiro256** seeded through SplitMix64: integer-only, so the sequence does not depend on platform,
    /// scripting backend or .NET runtime, unlike <see cref="Random"/>. Not thread-safe; give each thread or
    /// system its own stream (<see cref="Fork"/>).
    /// </summary>
    public sealed class DeterministicRandomService : IDeterministicRandom
    {
        private ulong _s0;
        private ulong _s1;
        private ulong _s2;
        private ulong _s3;

        public ulong Seed { get; }

        public DeterministicRandomService(ulong seed)
        {
            Seed = seed;
            var mix = seed;
            _s0 = SplitMix64(ref mix);
            _s1 = SplitMix64(ref mix);
            _s2 = SplitMix64(ref mix);
            _s3 = SplitMix64(ref mix);
        }

        public ulong NextUInt64()
        {
            var result = RotateLeft(_s1 * 5, 7) * 9;
            var t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);
            return result;
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive > maxExclusive)
                throw new ArgumentOutOfRangeException(nameof(minInclusive), minInclusive, "The minimum is above the maximum.");
            if (minInclusive == maxExclusive)
                return minInclusive;

            // Rejection keeps every value equally likely: the top of the 64-bit range that does not divide evenly is redrawn.
            var range = (ulong)((long)maxExclusive - minInclusive);
            var threshold = unchecked(0UL - range) % range;
            ulong value;
            do
            {
                value = NextUInt64();
            }
            while (value < threshold);

            return (int)(minInclusive + (long)(value % range));
        }

        public float NextFloat(float minInclusive, float maxExclusive)
            => (float)(minInclusive + NextDouble() * (maxExclusive - minInclusive));

        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public bool NextBool() => (NextUInt64() >> 63) == 0;

        public void Shuffle<T>(IList<T> values)
        {
            if (values == null)
                return;

            for (var i = values.Count - 1; i > 0; i--)
            {
                var j = NextInt(0, i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        public T NextWeighted<T>(IReadOnlyList<WeightedItem<T>> items) => WeightedItems.Pick(items, NextDouble());

        public RandomState CaptureState() => new(_s0, _s1, _s2, _s3);

        public void RestoreState(RandomState state)
        {
            if (!state.IsValid)
                throw new ArgumentException("An all-zero random state is not a state this generator can be in.", nameof(state));

            _s0 = state.S0;
            _s1 = state.S1;
            _s2 = state.S2;
            _s3 = state.S3;
        }

        public IDeterministicRandom Fork(string streamName)
        {
            if (string.IsNullOrEmpty(streamName))
                throw new ArgumentException("A stream needs a name.", nameof(streamName));

            var mix = Seed ^ StableHash(streamName);
            return new DeterministicRandomService(SplitMix64(ref mix));
        }

        private static ulong RotateLeft(ulong value, int shift) => (value << shift) | (value >> (64 - shift));

        private static ulong SplitMix64(ref ulong state)
        {
            state += 0x9E3779B97F4A7C15UL;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        // FNV-1a over the UTF-16 code units: string.GetHashCode differs between runtimes and processes.
        private static ulong StableHash(string value)
        {
            var hash = 14695981039346656037UL;
            foreach (var character in value)
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }

            return hash;
        }
    }

    public static class WeightedItems
    {
        /// <summary>Picks an item with odds proportional to its weight, from a roll in [0, 1).</summary>
        public static T Pick<T>(IReadOnlyList<WeightedItem<T>> items, double unitRoll)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("At least one weighted item is required.", nameof(items));
            if (!double.IsFinite(unitRoll) || unitRoll < 0d || unitRoll >= 1d)
                throw new ArgumentOutOfRangeException(nameof(unitRoll), unitRoll, "Roll must be finite and in [0, 1).");

            var totalWeight = 0.0;
            for (var i = 0; i < items.Count; i++)
            {
                totalWeight += items[i].Weight;
                if (!double.IsFinite(totalWeight))
                    throw new ArgumentException("Total weight must be finite.", nameof(items));
            }
            if (totalWeight <= 0)
                throw new ArgumentException("Total weight must be greater than zero.", nameof(items));

            var roll = unitRoll * totalWeight;
            var cumulative = 0.0;
            var lastPositive = -1;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Weight > 0)
                    lastPositive = i;
                cumulative += items[i].Weight;
                if (roll < cumulative)
                    return items[i].Value;
            }

            // Only reached via floating-point rounding at the very top of the range.
            return items[lastPositive].Value;
        }
    }
}
