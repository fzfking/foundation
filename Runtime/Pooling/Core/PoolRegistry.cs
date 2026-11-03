using System;
using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Pooling
{
    /// <summary>
    /// The live pools of the process, for editor and in-game diagnostics that cannot reach the container.
    /// No-ops outside the Editor and development builds. Holds pools weakly, so an undisposed pool and its
    /// instances are not kept alive.
    /// </summary>
    public static class PoolRegistry
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static readonly List<WeakReference<IPool>> Live = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Live.Clear();
#endif

        public static void Collect(List<IPool> results)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PruneDead();
            foreach (var reference in Live)
            {
                if (reference.TryGetTarget(out var pool))
                    results.Add(pool);
            }
#endif
        }

        internal static void Add(IPool pool)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            PruneDead();
            Live.Add(new WeakReference<IPool>(pool));
#endif
        }

        internal static void Remove(IPool pool)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            for (var index = Live.Count - 1; index >= 0; index--)
            {
                if (!Live[index].TryGetTarget(out var candidate) || ReferenceEquals(candidate, pool))
                    Live.RemoveAt(index);
            }
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static void PruneDead()
        {
            for (var index = Live.Count - 1; index >= 0; index--)
            {
                if (!Live[index].TryGetTarget(out _))
                    Live.RemoveAt(index);
            }
        }
#endif
    }
}
