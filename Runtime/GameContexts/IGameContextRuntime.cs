using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameContexts
{
    /// <summary>
    /// The one contract a context registers in its own scope. <see cref="StartAsync"/> owns everything from
    /// "the scope exists" to "the player can play" (resolve configs, restore the save, start simulation and
    /// UI); <see cref="StopAsync"/> quiesces it before the scope is disposed.
    /// <see cref="GameContextController"/> saves the namespace around <see cref="StopAsync"/>, so an
    /// implementation must not persist anything itself on the way out.
    /// </summary>
    public interface IGameContextRuntime
    {
        UniTask StartAsync(CancellationToken cancellationToken = default);

        UniTask StopAsync(CancellationToken cancellationToken = default);
    }
}
