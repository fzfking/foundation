using System;
using System.Collections.Generic;
using UnityEditor;

namespace Foundation.TypedIds.Editor
{
    /// <summary>
    /// Finds every definition of a stable id type through the <see cref="IStableIdProvider"/>s. Cached until
    /// the project changes or <see cref="Invalidate"/> is called, so it can be queried freely.
    /// </summary>
    public static class StableIdCatalog
    {
        private static readonly Dictionary<Type, List<StableIdEntry>> Cache = new();
        private static List<IStableIdProvider> _providers;

        /// <summary>Raised after the cache was dropped; open pickers refresh from it.</summary>
        public static event Action Changed;

        public static void Invalidate()
        {
            Cache.Clear();
            Changed?.Invoke();
        }

        public static IReadOnlyList<Type> GetIdTypes()
        {
            var types = new List<Type>();
            foreach (var provider in GetProviders())
            {
                if (!types.Contains(provider.IdType))
                    types.Add(provider.IdType);
            }

            return types;
        }

        public static bool HasProvider(Type idType)
        {
            foreach (var provider in GetProviders())
            {
                if (provider.IdType == idType)
                    return true;
            }

            return false;
        }

        public static IReadOnlyList<StableIdEntry> GetEntries(Type idType)
        {
            if (Cache.TryGetValue(idType, out var cached))
                return cached;

            var entries = new List<StableIdEntry>();
            foreach (var provider in GetProviders())
            {
                if (provider.IdType == idType)
                    provider.Collect(entries);
            }

            entries.Sort(static (left, right) => string.Compare(
                (left.Group ?? string.Empty) + "/" + left.DisplayName,
                (right.Group ?? string.Empty) + "/" + right.DisplayName,
                StringComparison.OrdinalIgnoreCase));
            Cache[idType] = entries;
            return entries;
        }

        public static bool TryFind(Type idType, string id, out StableIdEntry entry)
        {
            var entries = GetEntries(idType);
            for (var index = 0; index < entries.Count; index++)
            {
                if (string.Equals(entries[index].Id, id, StringComparison.Ordinal))
                {
                    entry = entries[index];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
        }

        private static List<IStableIdProvider> GetProviders()
        {
            if (_providers != null)
                return _providers;

            _providers = new();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IStableIdProvider>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                _providers.Add((IStableIdProvider)Activator.CreateInstance(type));
            }

            return _providers;
        }
    }
}
