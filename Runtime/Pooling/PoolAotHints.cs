using UnityEngine;
using UnityEngine.Scripting;

namespace Foundation.Pooling
{
    // Instantiates the generic pools once with reference types, so IL2CPP compiles their shared code in this AOT
    // assembly and hot-update (HybridCLR) code using Pool<T> or PrefabPool<T> with its own types does not hit a
    // missing AOT instantiation. Never called.
    [Preserve]
    internal static class PoolAotHints
    {
        [Preserve]
        private static void Instantiate()
        {
            Use(default(Pool<object>));
            Use(default(PrefabPool<Object>));
            Use(default(PrefabPool<GameObject>));
            Use(default(PrefabPool<Component>));
            new PoolLease<object>(null, null).Dispose();
            new PoolLease<Object>(null, null).Dispose();
            Use(default(DelegatePoolStrategy<object>));
            Use(default(KeyedViewSet<int, object>));
            Use(default(KeyedViewSet<string, object>));
        }

        private static void Use<T>(T value)
        {
        }
    }
}
