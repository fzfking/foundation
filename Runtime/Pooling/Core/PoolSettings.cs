using System;
using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Pooling
{
    public enum PoolExhaustedBehavior
    {
        /// <summary><c>Rent</c> returns null and <c>TryRent</c> returns false. Callers must handle it.</summary>
        ReturnNull,
        Throw,
    }

    /// <summary>
    /// Sizing and trimming rules of one pool. Serializable and immutable at runtime. Zero means no limit for
    /// maximums and off for idle trimming.
    /// </summary>
    [Serializable]
    public sealed class PoolSettings
    {
        [SerializeField, Min(0)] private int prewarmCount;
        [SerializeField, Min(0)] private int minRetained;
        [SerializeField, Min(0)] private int maxRetained;
        [SerializeField, Min(0)] private int maxActive;
        [SerializeField] private PoolExhaustedBehavior exhaustedBehavior;
        [SerializeField, Min(1)] private int growthBatch = 1;
        [SerializeField, Min(0f)] private float idleTrimSeconds;
        [SerializeField, Min(1)] private int trimBatchSize = 8;

        public static PoolSettings Default { get; } = new();

        /// <summary>Idle instances the pool should hold ready after prewarming (see <c>PrewarmAsync</c>).
        /// A pool never prewarms by itself; this is the target prewarm calls use.</summary>
        public int PrewarmCount => prewarmCount;

        /// <summary>Idle trimming never destroys below this many idle instances.</summary>
        public int MinRetained => minRetained;

        /// <summary>The most idle instances kept; a returned instance beyond it is destroyed. 0 = unlimited.</summary>
        public int MaxRetained => maxRetained;

        /// <summary>The most instances rented at once. 0 = unlimited.</summary>
        public int MaxActive => maxActive;

        public PoolExhaustedBehavior ExhaustedBehavior => exhaustedBehavior;

        /// <summary>How many instances are created when a rent finds the pool empty: one for the caller and
        /// the rest kept idle, so a burst pays creation cost once.</summary>
        public int GrowthBatch => growthBatch;

        /// <summary>Idle instances unused for this long are destroyed down to <see cref="MinRetained"/>.
        /// 0 disables idle trimming.</summary>
        public float IdleTrimSeconds => idleTrimSeconds;

        /// <summary>The most instances destroyed per trim check, so trimming never causes a spike.</summary>
        public int TrimBatchSize => trimBatchSize;

        public PoolSettings()
        {
        }

        public PoolSettings(
            int prewarmCount = 0,
            int minRetained = 0,
            int maxRetained = 0,
            int maxActive = 0,
            PoolExhaustedBehavior exhaustedBehavior = PoolExhaustedBehavior.ReturnNull,
            int growthBatch = 1,
            float idleTrimSeconds = 0f,
            int trimBatchSize = 8)
        {
            this.prewarmCount = prewarmCount;
            this.minRetained = minRetained;
            this.maxRetained = maxRetained;
            this.maxActive = maxActive;
            this.exhaustedBehavior = exhaustedBehavior;
            this.growthBatch = growthBatch;
            this.idleTrimSeconds = idleTrimSeconds;
            this.trimBatchSize = trimBatchSize;
            ThrowIfInvalid();
        }

        /// <summary>
        /// Reports configuration problems for editors and tests. With <paramref name="includeWarnings"/>
        /// false only the errors the constructor rejects are listed.
        /// </summary>
        public IReadOnlyList<string> Validate(bool includeWarnings = true)
        {
            var problems = new List<string>();
            if (prewarmCount < 0)
                problems.Add("Prewarm count must not be negative.");
            if (minRetained < 0 || maxRetained < 0 || maxActive < 0)
                problems.Add("Limits must not be negative.");
            if (maxRetained > 0 && minRetained > maxRetained)
                problems.Add("Min retained must not exceed max retained.");
            if (includeWarnings && maxRetained > 0 && prewarmCount > maxRetained)
                problems.Add("Prewarm count exceeds max retained, so the surplus would be destroyed.");
            if (growthBatch < 1)
                problems.Add("Growth batch must be at least 1.");
            if (idleTrimSeconds < 0f)
                problems.Add("Idle trim time must not be negative.");
            if (trimBatchSize < 1)
                problems.Add("Trim batch size must be at least 1.");
            return problems;
        }

        private void ThrowIfInvalid()
        {
            var problems = Validate(includeWarnings: false);
            if (problems.Count > 0)
                throw new ArgumentException("Invalid pool settings: " + string.Join(" ", problems));
        }
    }
}
