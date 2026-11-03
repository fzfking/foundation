namespace Foundation.Pooling
{
    /// <summary>A copy of a pool's counters. Cheap enough to take every frame.</summary>
    public readonly struct PoolStats
    {
        /// <summary>Instances ever created (prewarm, misses and growth).</summary>
        public int Created { get; }
        public int Active { get; }
        public int Inactive { get; }
        public int PeakActive { get; }
        public int RentCount { get; }
        public int ReturnCount { get; }

        /// <summary>Rents that found no idle instance (each one created something or was rejected).</summary>
        public int Misses { get; }
        public int Expansions { get; }

        /// <summary>Rents refused because <c>MaxActive</c> was reached.</summary>
        public int Rejected { get; }

        /// <summary>Instances destroyed by overflow, trimming, disposal or because the engine destroyed them.</summary>
        public int Destroyed { get; }

        /// <summary>
        /// A prewarm count that would have avoided every miss so far: the observed peak. A hint from data,
        /// not a guarantee.
        /// </summary>
        public int SuggestedPrewarm => PeakActive;

        public PoolStats(
            int created, int active, int inactive, int peakActive, int rentCount,
            int returnCount, int misses, int expansions, int rejected, int destroyed)
        {
            Created = created;
            Active = active;
            Inactive = inactive;
            PeakActive = peakActive;
            RentCount = rentCount;
            ReturnCount = returnCount;
            Misses = misses;
            Expansions = expansions;
            Rejected = rejected;
            Destroyed = destroyed;
        }
    }
}
