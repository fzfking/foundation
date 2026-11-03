using System;

namespace Foundation.Audio
{
    public interface IAudioSettings
    {
        float MasterVolume { get; }

        event Action Changed;

        float GetChannelVolume(string channelId);
        float GetOutputGroupVolume(string outputGroupId);

        /// <summary>A muted channel is silent whatever its volume is, and plays no new playbacks.</summary>
        bool IsChannelMuted(string channelId);

        void SetMasterVolume(float volume);
        void SetChannelVolume(string channelId, float volume);
        void SetOutputGroupVolume(string outputGroupId, float volume);
        void SetChannelMuted(string channelId, bool muted);
    }
}
