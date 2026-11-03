using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Lifecycle
{
    /// <summary>Runs once, after the scope that owns it is built.</summary>
    public interface IStartableEntryPoint
    {
        void Start();
    }

    /// <summary>Runs once, after the scope that owns it is built. The token is cancelled when the scope is disposed.</summary>
    public interface IAsyncStartableEntryPoint
    {
        UniTask StartAsync(CancellationToken cancellationToken);
    }

    /// <summary>Runs every frame, from the scope's build until its disposal.</summary>
    public interface ITickableEntryPoint
    {
        void Tick();
    }
}
