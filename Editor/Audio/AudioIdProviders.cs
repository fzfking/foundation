using System.Collections.Generic;
using Foundation.TypedIds.Editor;

namespace Foundation.Audio.Editor
{
    public sealed class AudioEventIdProvider : ScriptableObjectStableIdProvider<AudioEventId, AudioEventLibrary>
    {
        protected override void Describe(AudioEventLibrary library, List<StableIdEntry> entries)
        {
            foreach (var definition in library.Events)
            {
                var clipCount = definition.Clips?.Length ?? 0;
                entries.Add(new StableIdEntry(
                    definition.Id.Value,
                    definition.DisplayName,
                    definition.Category,
                    library,
                    $"{definition.Channel.Value} · {clipCount} clip{(clipCount == 1 ? string.Empty : "s")}"));
            }
        }
    }

    public sealed class AudioChannelIdProvider : ScriptableObjectStableIdProvider<AudioChannelId, AudioChannelCatalog>
    {
        protected override void Describe(AudioChannelCatalog catalog, List<StableIdEntry> entries)
        {
            foreach (var channel in catalog.Channels)
                entries.Add(new StableIdEntry(channel.Id.Value, channel.DisplayName, null, catalog));
        }
    }
}
