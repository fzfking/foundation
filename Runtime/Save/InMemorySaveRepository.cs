using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Save
{
    public sealed class InMemorySaveRepository : ISaveRepositoryBackend
    {
        private readonly Dictionary<string, SaveData> _dataBySlot = new(StringComparer.Ordinal);

        public UniTask<SaveData> LoadAsync(CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(_dataBySlot.TryGetValue(slotId, out var data) ? Clone(data) : null);
        }

        public UniTask SaveAsync(SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _dataBySlot[slotId] = Clone(data ?? throw new ArgumentNullException(nameof(data)));
            return UniTask.CompletedTask;
        }

        private static SaveData Clone(SaveData data)
        {
            var segments = new Dictionary<string, byte[]>(data.Segments.Count, StringComparer.Ordinal);
            foreach (var (segmentId, bytes) in data.Segments)
                segments.Add(segmentId, (byte[])bytes?.Clone());

            return new SaveData(data.Version, segments, data.SavedAtUtcUnixMilliseconds);
        }
    }
}
