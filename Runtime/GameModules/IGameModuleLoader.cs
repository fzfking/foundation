using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameModules
{
    /// <summary>
    /// Resolves a <see cref="GameModuleId"/> into a loaded <see cref="IGameModule"/>. The one interface
    /// HybridCLR (or any dynamic loader) may sit behind: callers see <see cref="IGameModuleHandle.Module"/>,
    /// never an assembly or reflection.
    /// </summary>
    public interface IGameModuleLoader
    {
        UniTask<IGameModuleHandle> LoadAsync(GameModuleId moduleId, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents the logical lifetime of a loaded module and any resources its loader keeps alive. Physical unload is
    /// not promised: Community HybridCLR cannot unload an IL2CPP-hosted assembly. The HybridCLR loader releases the
    /// source content handle after copying/verifying the bytes; disposing its returned handle does not unload the
    /// assembly or its types. A loader that supports physical unload can release its resources here.
    /// </summary>
    public interface IGameModuleHandle : IDisposable
    {
        IGameModule Module { get; }
    }
}
