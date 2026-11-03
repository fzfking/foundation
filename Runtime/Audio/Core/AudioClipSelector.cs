using System;
using Foundation.Randomization;

namespace Foundation.Audio
{
    /// <summary>Picks which clip of an event plays next. Pure: no Unity types, fully deterministic with a seeded random.</summary>
    public static class AudioClipSelector
    {
        /// <summary>
        /// The index to play, or -1 when nothing can be played. Entries that are empty or weigh nothing are never picked;
        /// <paramref name="lastIndex"/> is the index picked last time, or -1.
        /// </summary>
        public static int Pick(AudioSelectionMode mode, AudioClipEntry[] clips, int lastIndex, IRandomService random)
        {
            if (clips == null || clips.Length == 0)
                return -1;

            switch (mode)
            {
                case AudioSelectionMode.Single:
                    return FirstUsable(clips);
                case AudioSelectionMode.Random:
                    return PickUniform(clips, -1, random);
                case AudioSelectionMode.RandomNoImmediateRepeat:
                    return PickUniform(clips, lastIndex, random);
                case AudioSelectionMode.WeightedRandom:
                    return PickWeighted(clips, random);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static int FirstUsable(AudioClipEntry[] clips)
        {
            for (var index = 0; index < clips.Length; index++)
            {
                if (!clips[index].IsEmpty)
                    return index;
            }

            return -1;
        }

        private static int PickUniform(AudioClipEntry[] clips, int excluded, IRandomService random)
        {
            var usable = 0;
            for (var index = 0; index < clips.Length; index++)
                if (!clips[index].IsEmpty && index != excluded)
                    usable++;
            
            if (usable == 0)
                return excluded >= 0 && excluded < clips.Length && !clips[excluded].IsEmpty ? excluded : -1;

            var pick = random.NextInt(0, usable);
            for (var index = 0; index < clips.Length; index++)
            {
                if (clips[index].IsEmpty || index == excluded)
                    continue;

                if (pick-- == 0)
                    return index;
            }

            return -1;
        }

        private static int PickWeighted(AudioClipEntry[] clips, IRandomService random)
        {
            var total = 0d;
            foreach (var clipEntry in clips)
                total += clipEntry.Weight;

            if (total <= 0d)
                return -1;

            var roll = random.NextDouble() * total;
            var last = -1;
            for (var index = 0; index < clips.Length; index++)
            {
                var weight = clips[index].Weight;
                if (weight <= 0d)
                    continue;

                last = index;
                roll -= weight;
                if (roll < 0d)
                    return index;
            }

            return last;
        }
    }
}
