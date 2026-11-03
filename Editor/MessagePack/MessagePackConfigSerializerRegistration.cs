using UnityEditor;
using Foundation.Content.Editor;

namespace Foundation.Infrastructure.MessagePack.Editor
{
    internal static class MessagePackConfigSerializerRegistration
    {
        [InitializeOnLoadMethod]
        private static void Register()
            => ConfigSerializers.Register("MessagePack", () => new MessagePackSerializerAdapter());
    }
}
