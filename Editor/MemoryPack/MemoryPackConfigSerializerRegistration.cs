using UnityEditor;
using Foundation.Content.Editor;

namespace Foundation.Infrastructure.MemoryPack.Editor
{
    internal static class MemoryPackConfigSerializerRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
            => ConfigSerializers.Register("MemoryPack", () => new MemoryPackSerializerAdapter());
    }
}
