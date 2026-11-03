using System;
using System.Collections.Generic;
using Foundation.DI;

namespace Foundation.GameContexts
{
    /// <summary>
    /// The authored description of one game context: its id, save namespace and the installers that compose
    /// its scope. A new context (a seasonal event, a second mode) is a new definition plus its installers and
    /// config set, not a new class hierarchy.
    /// It says nothing about how gameplay is implemented; the only contract the controller needs from inside
    /// the scope is <see cref="IGameContextRuntime"/>.
    /// </summary>
    public sealed class GameContextDefinition
    {
        public GameContextId Id { get; }

        public string DisplayName { get; }

        public GameContextSaveProfile Save { get; }

        /// <summary>
        /// Everything the context owns: runtime state, simulation, features, tutorial, scoped UI, its own
        /// config repository and its <see cref="IGameContextRuntime"/>. Installed into the child scope, so it
        /// is created on enter and disposed on exit. Application-owned services (content, localization,
        /// analytics, audio, ads, save backend, loading UI) must not be registered here.
        /// </summary>
        public IReadOnlyList<IFeatureInstaller> Installers { get; }

        public GameContextDefinition(
            GameContextId id,
            GameContextSaveProfile save,
            IEnumerable<IFeatureInstaller> installers,
            string displayName = null)
        {
            if (id.IsEmpty)
                throw new ArgumentException("Game context definition requires a non-empty id.", nameof(id));

            Id = id;
            Save = save ?? throw new ArgumentNullException(nameof(save));
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? id.Value : displayName;

            var resolved = new List<IFeatureInstaller>();
            foreach (var installer in installers ?? throw new ArgumentNullException(nameof(installers)))
            {
                if (installer == null)
                    throw new ArgumentException($"Game context '{id}' has a null installer.", nameof(installers));

                resolved.Add(installer);
            }

            if (resolved.Count == 0)
                throw new ArgumentException($"Game context '{id}' has no installers.", nameof(installers));

            Installers = resolved;
        }

        public override string ToString() => $"{Id} (save namespace '{Save.Namespace}')";
    }
}
