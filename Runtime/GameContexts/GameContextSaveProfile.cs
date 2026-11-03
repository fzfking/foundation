using System;
using System.Collections.Generic;
using Foundation.Save;

namespace Foundation.GameContexts
{
    /// <summary>
    /// How one game context persists: the save namespace it owns and its own version/migration chain.
    /// Contexts are separate games, so a seasonal event bumping its save version must not migrate the main
    /// save.
    /// The namespace maps onto the save system's existing <c>slotId</c>. <see cref="SlotId"/> can be pinned
    /// apart from <see cref="Namespace"/>, so a game that shipped on <see cref="SaveSlot.Default"/> can adopt
    /// a named namespace without orphaning existing saves.
    /// </summary>
    public sealed class GameContextSaveProfile
    {
        private static readonly ISaveMigration[] NoMigrations = Array.Empty<ISaveMigration>();

        /// <summary>Logical name of the context's save namespace ("main", "halloween-2026", ...).</summary>
        public string Namespace { get; }

        /// <summary>
        /// The storage slot the namespace resolves to: <see cref="Namespace"/> by default,
        /// <see cref="SaveSlot.Default"/> for a context whose saves predate namespacing.
        /// </summary>
        public string SlotId { get; }

        public int SaveVersion { get; }

        public IReadOnlyList<ISaveMigration> Migrations { get; }

        public GameContextSaveProfile(
            string saveNamespace,
            int saveVersion,
            IEnumerable<ISaveMigration> migrations = null,
            string slotId = null)
        {
            if (string.IsNullOrWhiteSpace(saveNamespace))
                throw new ArgumentException("Save namespace must not be empty.", nameof(saveNamespace));

            if (saveVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(saveVersion), saveVersion, "Save version must be positive.");

            Namespace = saveNamespace;
            SlotId = string.IsNullOrWhiteSpace(slotId) ? saveNamespace : slotId;
            SaveVersion = saveVersion;
            Migrations = migrations == null ? NoMigrations : new List<ISaveMigration>(migrations);
        }

        /// <summary>
        /// A namespace that keeps writing to the pre-namespacing default slot, for an already shipped game's
        /// context: the namespace becomes explicit while the save file stays where players' data is.
        /// </summary>
        public static GameContextSaveProfile Legacy(
            string saveNamespace,
            int saveVersion,
            IEnumerable<ISaveMigration> migrations = null)
            => new(saveNamespace, saveVersion, migrations, SaveSlot.Default);
    }
}
