using System;
using System.Collections.Generic;
using Foundation.GameModules;

namespace Foundation.GameContexts
{
    /// <summary>
    /// AOT metadata needed to select and prepare a context before its implementation assembly is available:
    /// logical module ids and save metadata only.
    /// </summary>
    public sealed class GameContextDescriptor
    {
        public GameContextId Id { get; }

        public GameContextSaveProfile Save { get; }

        public IReadOnlyList<GameModuleId> Modules { get; }

        public string DisplayName { get; }

        /// <summary>The season of a seasonal event this context is; null when it is not seasonal.</summary>
        public string SeasonId { get; }

        public GameContextAvailability Availability { get; }

        public GameContextDescriptor(
            GameContextId id,
            GameContextSaveProfile save,
            IReadOnlyList<GameModuleId> modules,
            string displayName = null,
            string seasonId = null,
            GameContextAvailability availability = default)
        {
            if (id.IsEmpty)
                throw new ArgumentException("A context descriptor requires a non-empty id.", nameof(id));

            Id = id;
            Save = save ?? throw new ArgumentNullException(nameof(save));
            if (modules == null || modules.Count == 0)
                throw new ArgumentException("A context descriptor requires at least one module.", nameof(modules));

            var copy = new List<GameModuleId>(modules.Count);
            var uniqueModules = new HashSet<GameModuleId>();
            foreach (var module in modules)
            {
                if (string.IsNullOrWhiteSpace(module.Value))
                    throw new ArgumentException($"Context '{id}' contains an empty module id.", nameof(modules));
                if (!uniqueModules.Add(module))
                    throw new ArgumentException($"Context '{id}' lists module '{module}' more than once.", nameof(modules));
                copy.Add(module);
            }

            Modules = copy.AsReadOnly();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;
            SeasonId = string.IsNullOrWhiteSpace(seasonId) ? null : seasonId;
            Availability = availability;
        }
    }
}
