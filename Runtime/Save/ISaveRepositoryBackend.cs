using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Save
{
    public interface ISaveRepositoryBackend : ISaveRepository
    {
    }

    public enum SaveWriteResult
    {
        Skipped,
        Persisted,
    }

    /// <summary>A backend that can defer or skip a write reports that separately from durable persistence.</summary>
    public interface ISaveRepositoryBackendWithResult : ISaveRepositoryBackend
    {
        UniTask<SaveWriteResult> SaveWithResultAsync(
            SaveData data, CancellationToken cancellationToken = default, string slotId = SaveSlot.Default);
    }
}
