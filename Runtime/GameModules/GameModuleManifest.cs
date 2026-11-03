using System;
using System.Collections.Generic;

namespace Foundation.GameModules
{
    /// <summary>Logical module identity mapped to delivery metadata. Assembly addresses never
    /// become part of the GameModuleId contract.</summary>
    public sealed class GameModuleManifestEntry
    {
        public GameModuleId Id { get; }

        public string AssemblyAddress { get; }

        public IReadOnlyList<GameModuleId> Dependencies { get; }

        public GameModuleManifestEntry(
            GameModuleId id,
            string assemblyAddress,
            IReadOnlyList<GameModuleId> dependencies = null)
        {
            if (string.IsNullOrWhiteSpace(id.Value))
                throw new ArgumentException("A manifest entry requires a module id.", nameof(id));
            if (string.IsNullOrWhiteSpace(assemblyAddress))
                throw new ArgumentException("A manifest entry requires an assembly address.", nameof(assemblyAddress));

            Id = id;
            AssemblyAddress = assemblyAddress;
            var copy = dependencies == null ? new List<GameModuleId>() : new List<GameModuleId>(dependencies);
            if (copy.Contains(id))
                throw new ArgumentException($"Module '{id}' cannot depend on itself.", nameof(dependencies));
            Dependencies = copy.AsReadOnly();
        }
    }

    public sealed class GameModuleManifest
    {
        private readonly Dictionary<GameModuleId, GameModuleManifestEntry> _entries = new();
        private readonly List<GameModuleManifestEntry> _entryList = new();
        private readonly IReadOnlyList<GameModuleManifestEntry> _entriesView;

        public IReadOnlyList<GameModuleManifestEntry> Entries => _entriesView;

        public GameModuleManifest(IEnumerable<GameModuleManifestEntry> entries)
        {
            foreach (var entry in entries ?? throw new ArgumentNullException(nameof(entries)))
            {
                if (entry == null)
                    throw new ArgumentException("Module manifest contains a null entry.", nameof(entries));
                if (!_entries.TryAdd(entry.Id, entry))
                    throw new InvalidOperationException($"Module '{entry.Id}' is present more than once in the manifest.");
                _entryList.Add(entry);
            }

            foreach (var entry in _entryList)
                foreach (var dependency in entry.Dependencies)
                    if (!_entries.ContainsKey(dependency))
                        throw new InvalidOperationException($"Module '{entry.Id}' depends on unlisted module '{dependency}'.");

            ValidateAcyclicDependencies();
            _entriesView = _entryList.AsReadOnly();
        }

        public bool TryGet(GameModuleId id, out GameModuleManifestEntry entry)
            => _entries.TryGetValue(id, out entry);

        public GameModuleManifestEntry Get(GameModuleId id)
            => _entries.TryGetValue(id, out var entry)
                ? entry
                : throw new InvalidOperationException($"No module manifest entry exists for '{id}'.");

        private void ValidateAcyclicDependencies()
        {
            var visited = new HashSet<GameModuleId>();
            var active = new HashSet<GameModuleId>();
            foreach (var entry in _entryList)
                Visit(entry.Id, visited, active);
        }

        private void Visit(GameModuleId id, HashSet<GameModuleId> visited, HashSet<GameModuleId> active)
        {
            if (visited.Contains(id))
                return;
            if (!active.Add(id))
                throw new InvalidOperationException($"Game module dependency cycle detected at '{id}'.");

            foreach (var dependency in _entries[id].Dependencies)
                Visit(dependency, visited, active);

            active.Remove(id);
            visited.Add(id);
        }
    }
}
