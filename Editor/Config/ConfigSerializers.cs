using System;
using System.Collections.Generic;
using System.Linq;
using Foundation.Serialization;

namespace Foundation.Content.Editor
{
    /// <summary>
    /// Where the content build gets its binary format. A project states it with <see cref="ConfigBuildSettings.Serializer"/>.
    /// Without that, the serializer adapters installed in the project announce themselves here (MemoryPack and MessagePack
    /// each ship a small editor assembly that does), and the build uses the only one found. With several installed the
    /// project has to choose, since reading a file with another format than it was written in only fails at runtime.
    /// </summary>
    public static class ConfigSerializers
    {
        private static readonly Dictionary<string, Func<ISerializer>> Candidates = new(StringComparer.Ordinal);

        /// <summary>Announces an installed serializer; a later registration under the same name replaces the earlier one.</summary>
        public static void Register(string name, Func<ISerializer> factory)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A serializer name is required.", nameof(name));

            Candidates[name] = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        internal static bool TryResolve(ISerializer explicitSerializer, out ISerializer serializer, out string problem)
            => TryResolve(explicitSerializer, Candidates, out serializer, out problem);

        internal static bool TryResolve(
            ISerializer explicitSerializer,
            IReadOnlyDictionary<string, Func<ISerializer>> candidates,
            out ISerializer serializer,
            out string problem)
        {
            problem = null;
            if (explicitSerializer != null)
            {
                serializer = explicitSerializer;
                return true;
            }

            serializer = null;
            if (candidates.Count == 1)
            {
                serializer = candidates.Values.First()();
                return true;
            }

            problem = candidates.Count == 0
                ? "No serializer for the built files: install a serializer adapter (MemoryPack or MessagePack) or set "
                  + $"{nameof(ConfigBuildSettings)}.{nameof(ConfigBuildSettings.Serializer)}."
                : $"Several serializer adapters are installed ({string.Join(", ", candidates.Keys.OrderBy(name => name, StringComparer.Ordinal))}): "
                  + $"choose the one the game reads with {nameof(ConfigBuildSettings)}.{nameof(ConfigBuildSettings.Serializer)}.";
            return false;
        }
    }
}
