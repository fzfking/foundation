using System;

namespace Foundation.Randomization
{
    public readonly struct DiceRoll
    {
        public int Sides { get; }

        public int Result { get; }

        public DiceRoll(int sides, int result)
        {
            Sides = sides;
            Result = result;
        }

        public bool Is(int face) => Result == face;

        public bool IsAbove(int face) => Result > face;

        public bool IsAtLeast(int face) => Result >= face;

        public bool IsBelow(int face) => Result < face;

        public bool IsAtMost(int face) => Result <= face;
    }

    public static class RandomServiceDiceExtensions
    {
        public static DiceRoll RollDice(this IRandomService random, int sides)
        {
            if (random == null)
                throw new ArgumentNullException(nameof(random));
            if (sides < 1)
                throw new ArgumentOutOfRangeException(nameof(sides), sides, "A die needs at least one side.");

            return new DiceRoll(sides, random.NextInt(1, sides + 1));
        }
    }
}
