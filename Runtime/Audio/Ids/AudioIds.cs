using Foundation.TypedIds;

namespace Foundation.Audio
{
    [StableId]
    public partial struct AudioEventId
    {
    }

    /// <summary>
    /// Identifies an audio channel (bus): Music, SFX, UI, Voice... The set is defined by the project; the
    /// value equals <see cref="IAudioChannelDefinition.Id"/>.
    /// </summary>
    [StableId]
    public partial struct AudioChannelId
    {
    }
}
