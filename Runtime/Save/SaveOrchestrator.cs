using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Save
{
    public sealed class SaveOrchestrator
    {
        public const int CurrentVersion = 1;

        private readonly IReadOnlyList<IFeatureSaveModule> _modules;
        private readonly ISaveRepository _repository;
        private readonly ISaveSerializer _serializer;
        private readonly int _currentVersion;
        private readonly IReadOnlyList<ISaveMigration> _migrations;

        // A context binds this to its own save namespace, so gameplay calls SaveAsync()/LoadAsync() without
        // arguments and no feature knows which context it runs in. Defaults to the single save from before
        // namespacing.
        private readonly string _defaultSlotId;

        // Callers are uncoordinated (checkpoint, prestige reset, ending, boot load). Without the gate two
        // overlapping saves race on one file and the later finisher wins, whichever captured older state.
        private readonly SemaphoreSlim _gate = new(1, 1);

        // A failed load blocks only its slot until that same slot loads successfully.
        private readonly HashSet<string> _failedSlots = new(StringComparer.Ordinal);

        // The newest save time this instance has loaded or written; the next save is stamped later than it.
        private long _lastStamp;

        public SaveOrchestrator(
            IEnumerable<IFeatureSaveModule> modules,
            ISaveRepository repository,
            ISaveSerializer serializer,
            int currentVersion = CurrentVersion,
            IEnumerable<ISaveMigration> migrations = null,
            string defaultSlotId = SaveSlot.Default)
        {
            _modules = ValidateModules(modules);
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _currentVersion = currentVersion;
            _migrations = ValidateMigrations(migrations);
            _defaultSlotId = string.IsNullOrWhiteSpace(defaultSlotId) ? SaveSlot.Default : defaultSlotId;
        }

        public async UniTask SaveAsync(CancellationToken cancellationToken = default, string slotId = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Segments are captured inside the gate: a queued save must snapshot state as of when
            // it runs, or "await SaveAsync() means my state is persisted" stops being true.
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var resolvedSlot = slotId ?? _defaultSlotId;
                if (_failedSlots.Contains(resolvedSlot))
                {
                    throw new InvalidOperationException(
                        "Refusing to save: the last load of this save failed, so what is stored is unknown and must not be overwritten.");
                }

                var segments = new Dictionary<string, byte[]>(_modules.Count, StringComparer.Ordinal);
                foreach (var module in _modules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    segments.Add(module.SegmentId, module.Capture(_serializer));
                }

                await _repository.SaveAsync(new SaveData(_currentVersion, segments, NextStamp()), cancellationToken,
                    slotId ?? _defaultSlotId);
            }
            finally
            {
                _gate.Release();
            }
        }

        public async UniTask<bool> LoadAsync(CancellationToken cancellationToken = default, string slotId = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _gate.WaitAsync(cancellationToken);
            try
            {
                var resolvedSlot = slotId ?? _defaultSlotId;
                _failedSlots.Add(resolvedSlot);
                var data = await _repository.LoadAsync(cancellationToken, resolvedSlot);
                if (data == null)
                {
                    _failedSlots.Remove(resolvedSlot);
                    return false;
                }

                if (data.Version > _currentVersion)
                {
                    throw new InvalidOperationException(
                        $"Save data version {data.Version} is newer than the supported version {_currentVersion}.");
                }

                if (data.Version < _currentVersion)
                    data = MigrateChain(data);

                RestoreSegments(data, cancellationToken);

                _failedSlots.Remove(resolvedSlot);
                _lastStamp = Math.Max(_lastStamp, data.SavedAtUtcUnixMilliseconds);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        // A failed restore must not leave the game half-restored: every module the load has started to change is put back
        // to its state from before the load, last one first. The slot stays blocked either way (see LoadAsync).
        private void RestoreSegments(SaveData data, CancellationToken cancellationToken)
        {
            var applied = new List<(IFeatureSaveModule Module, byte[] Before)>();
            var segmentId = string.Empty;
            try
            {
                foreach (var module in _modules)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // A segment written as null is the same as one that was never written.
                    if (!data.Segments.TryGetValue(module.SegmentId, out var bytes) || bytes == null)
                        continue;

                    segmentId = module.SegmentId;
                    applied.Add((module, module.Capture(_serializer)));
                    module.Restore(_serializer, bytes);
                }
            }
            catch (Exception exception)
            {
                var rollbackFailures = RollBack(applied);
                if (rollbackFailures == null)
                {
                    if (exception is OperationCanceledException)
                        throw;

                    throw new InvalidOperationException(
                        $"Failed to restore save segment '{segmentId}'; the modules it had changed were rolled back.", exception);
                }

                var failures = new List<Exception> { exception };
                failures.AddRange(rollbackFailures);
                throw new InvalidOperationException(
                    "Restoring the save failed and rolling the game state back failed too; the state is unknown.",
                    new AggregateException(failures));
            }
        }

        // A module that cannot be put back is reported together with the others, never skipped.
        private List<Exception> RollBack(List<(IFeatureSaveModule Module, byte[] Before)> applied)
        {
            List<Exception> failures = null;
            for (var index = applied.Count - 1; index >= 0; index--)
            {
                try
                {
                    applied[index].Module.Restore(_serializer, applied[index].Before);
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            return failures;
        }

        /// <summary>
        /// The time of the next save: now, but never earlier than a save this instance loaded or wrote.
        /// This keeps timestamps monotonic within the orchestrator; it does not correct clock skew between
        /// devices. Also stamps a save written past this orchestrator, such as a reset of its slot.
        /// </summary>
        public long NextStamp()
        {
            _lastStamp = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _lastStamp + 1);
            return _lastStamp;
        }

        private SaveData MigrateChain(SaveData data)
        {
            var current = data;
            while (current.Version < _currentVersion)
            {
                var migration = FindMigration(current.Version);
                if (migration == null)
                {
                    throw new InvalidOperationException(
                        $"No save migration registered from version {current.Version} to reach {_currentVersion}.");
                }

                if (migration.ToVersion > _currentVersion)
                    throw new InvalidOperationException(
                        $"Migration to version {migration.ToVersion} exceeds the supported version {_currentVersion}.");

                var migrated = migration.Migrate(current)
                    ?? throw new InvalidOperationException(
                        $"Migration from {migration.FromVersion} to {migration.ToVersion} returned null.");

                if (migrated.Version != migration.ToVersion)
                {
                    throw new InvalidOperationException(
                        $"Migration declared ToVersion {migration.ToVersion} but produced data with version {migrated.Version}.");
                }

                current = migrated;
            }

            return current;
        }

        private ISaveMigration FindMigration(int fromVersion)
        {
            for (var index = 0; index < _migrations.Count; index++)
            {
                if (_migrations[index].FromVersion == fromVersion)
                    return _migrations[index];
            }

            return null;
        }

        private static IReadOnlyList<IFeatureSaveModule> ValidateModules(IEnumerable<IFeatureSaveModule> modules)
        {
            if (modules == null)
                throw new ArgumentNullException(nameof(modules));

            var result = new List<IFeatureSaveModule>();
            var segmentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var module in modules)
            {
                if (module == null)
                    throw new ArgumentException("Save module collection contains null.", nameof(modules));

                if (string.IsNullOrWhiteSpace(module.SegmentId))
                    throw new ArgumentException("Save module SegmentId must not be empty.", nameof(modules));

                if (!segmentIds.Add(module.SegmentId))
                    throw new InvalidOperationException($"Duplicate save segment id '{module.SegmentId}'.");

                result.Add(module);
            }

            return result;
        }

        private static IReadOnlyList<ISaveMigration> ValidateMigrations(IEnumerable<ISaveMigration> migrations)
        {
            if (migrations == null)
                return Array.Empty<ISaveMigration>();

            var result = new List<ISaveMigration>();
            var seenFromVersions = new HashSet<int>();
            foreach (var migration in migrations)
            {
                if (migration == null)
                    throw new ArgumentException("Migration collection contains null.", nameof(migrations));

                if (migration.FromVersion >= migration.ToVersion)
                {
                    throw new ArgumentException(
                        $"Migration FromVersion ({migration.FromVersion}) must be less than ToVersion ({migration.ToVersion}).",
                        nameof(migrations));
                }

                if (!seenFromVersions.Add(migration.FromVersion))
                    throw new InvalidOperationException($"Duplicate migration registered FromVersion {migration.FromVersion}.");

                result.Add(migration);
            }

            return result;
        }
    }
}
