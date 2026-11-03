using System;
using System.Collections.Generic;

namespace Foundation.Randomization
{
    /// <summary>
    /// <see cref="Random"/> behind <see cref="IRandomService"/>, for cosmetic randomness. Its state cannot be
    /// saved; simulation and anything a save must reproduce uses <see cref="DeterministicRandomService"/>.
    /// </summary>
    public sealed class SystemRandomService : IRandomService
    {
        private readonly Random _random;

        public SystemRandomService() : this(new Random())
        {
        }

        public SystemRandomService(int seed) : this(new Random(seed))
        {
        }

        private SystemRandomService(Random random)
        {
            _random = random;
        }

        public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
        public float NextFloat(float minInclusive, float maxExclusive) =>
            (float)(minInclusive + _random.NextDouble() * (maxExclusive - minInclusive));

        public double NextDouble() => _random.NextDouble();
        public bool NextBool() => _random.Next(2) == 0;
        public void Shuffle<T>(IList<T> values)
        {
            if (values == null)
                return;

            for (var i = values.Count - 1; i > 0; i--)
            {
                var j = _random.Next(i + 1);
                (values[i], values[j]) = (values[j], values[i]);
            }
        }

        public T NextWeighted<T>(IReadOnlyList<WeightedItem<T>> items) => WeightedItems.Pick(items, _random.NextDouble());
    }
}
