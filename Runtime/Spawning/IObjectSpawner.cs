using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Spawning
{
    /// <summary>
    /// The one way to create objects from a prefab. Everything that called <c>Object.Instantiate</c> or
    /// <c>Addressables.InstantiateAsync</c> goes through here, so the game shares one integration budget.
    /// </summary>
    public interface IObjectSpawner
    {
        UniTask<T> InstantiateAsync<T>(
            T original,
            Transform parent = null,
            CancellationToken cancellationToken = default)
            where T : Object;
    }
}
