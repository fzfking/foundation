using System;
using System.Collections.Generic;

namespace Foundation.Randomization
{
    public readonly struct WeightedItem<T>
    {
        public T Value { get; }

        public double Weight { get; }

        public WeightedItem(T value, double weight)
        {
            if (!double.IsFinite(weight) || weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "Weight must be finite and non-negative.");

            Value = value;
            Weight = weight;
        }
    }

    public interface IRandomService
    {
        int NextInt(int minInclusive, int maxExclusive);
        float NextFloat(float minInclusive, float maxExclusive);
        double NextDouble();
        bool NextBool();
        void Shuffle<T>(IList<T> values);

        // Odds are proportional to weight; a zero-weight item can never be picked.
        T NextWeighted<T>(IReadOnlyList<WeightedItem<T>> items);
    }
}
