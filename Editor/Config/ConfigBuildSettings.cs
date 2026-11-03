using Foundation.Serialization;

namespace Foundation.Content.Editor
{
    public sealed class ConfigBuildSettings
    {
        public string OutputDirectory { get; set; } = "Assets/Generated/Config";

        /// <summary>
        /// The binary format of the built config files; it has to be the one the game's runtime
        /// <see cref="ISerializer"/> reads. Left null, the build uses the serializer adapter installed in the project (see
        /// <see cref="ConfigSerializers"/>).
        /// </summary>
        public ISerializer Serializer { get; set; }
    }
}
