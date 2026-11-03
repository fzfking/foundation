using System.Runtime.CompilerServices;

namespace Foundation.Pooling
{
    internal static class PoolOwnership
    {
        private static readonly ConditionalWeakTable<object, IPool> Owners = new();
        public static void Register(object item, IPool pool) => Owners.AddOrUpdate(item, pool);
        public static IPool OwnerOf(object item) => Owners.TryGetValue(item, out var pool) ? pool : null;
        public static void Unregister(object item) => Owners.Remove(item);
    }
}
