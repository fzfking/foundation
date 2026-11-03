using System.Threading;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace Foundation.Save
{
    public interface ISaveRepository
    {
        UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default);
        UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default);
    }

    public sealed class CompositeSaveRepository : ISaveRepository
    {
        private readonly IReadOnlyList<ISaveRepositoryBackend> _backends;

        public CompositeSaveRepository(IEnumerable<ISaveRepositoryBackend> backends)
        {
            if (backends == null)
                throw new ArgumentNullException(nameof(backends));
            _backends = new List<ISaveRepositoryBackend>(backends);
            if (_backends.Count == 0)
                throw new ArgumentException("At least one save backend is required.", nameof(backends));
        }

        public async UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            SaveData latest = null;
            List<Exception> failures = null;
            var loadTasks = new UniTask<LoadAttempt>[_backends.Count];
            for (var i = 0; i < _backends.Count; i++)
                loadTasks[i] = LoadBackendAsync(_backends[i], cancellationToken, slotId);
            var attempts = await UniTask.WhenAll(loadTasks);
            foreach (var attempt in attempts)
            {
                if (attempt.Data != null && (latest == null || attempt.Data.SavedAtUtcUnixMilliseconds > latest.SavedAtUtcUnixMilliseconds))
                    latest = attempt.Data;
                if (attempt.Error != null)
                    (failures ??= new List<Exception>()).Add(attempt.Error);
            }

            // A backend that could not be read may hold the save, so "nothing found" is unknown while one failed.
            // Returning null then would start a new game, and its first save would overwrite the unread one.
            if (latest == null && failures != null)
                throw new AggregateException(
                    failures.Count == _backends.Count
                        ? "All save backends failed to load."
                        : "A save backend failed to load and no backend returned a save.",
                    failures);
            return latest;
        }

        public async UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            List<Exception> failures = null;
            var saveTasks = new UniTask<SaveAttempt>[_backends.Count];
            for (var i = 0; i < _backends.Count; i++)
                saveTasks[i] = SaveBackendAsync(_backends[i], data, cancellationToken, slotId);
            var attempts = await UniTask.WhenAll(saveTasks);
            var persisted = false;
            foreach (var attempt in attempts)
            {
                persisted |= attempt.Persisted;
                if (attempt.Error != null)
                    (failures ??= new List<Exception>()).Add(attempt.Error);
            }

            if (!persisted)
                throw new AggregateException("No save backend persisted the save.", failures ?? new List<Exception>());
        }

        private static async UniTask<LoadAttempt> LoadBackendAsync(ISaveRepositoryBackend backend, CancellationToken token, string slotId)
        {
            try
            {
                return new LoadAttempt(await backend.LoadAsync(token, slotId), null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new LoadAttempt(null, exception);
            }
        }

        private static async UniTask<SaveAttempt> SaveBackendAsync(ISaveRepositoryBackend backend, SaveData data, CancellationToken token, string slotId)
        {
            try
            {
                if (backend is ISaveRepositoryBackendWithResult reporting)
                    return new SaveAttempt(await reporting.SaveWithResultAsync(data, token, slotId) == SaveWriteResult.Persisted, null);

                await backend.SaveAsync(data, token, slotId);
                return new SaveAttempt(true, null);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                return new SaveAttempt(false, exception);
            }
        }

        private readonly struct SaveAttempt
        {
            public bool Persisted { get; }
            public Exception Error { get; }

            public SaveAttempt(bool persisted, Exception error)
            {
                Persisted = persisted;
                Error = error;
            }
        }

        private readonly struct LoadAttempt
        {
            public SaveData Data { get; }
            public Exception Error { get; }

            public LoadAttempt(SaveData data, Exception error) { Data = data; Error = error; }
        }
    }
}
