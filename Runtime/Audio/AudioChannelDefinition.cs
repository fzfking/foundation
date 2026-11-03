using System;
namespace Foundation.Audio
{
    public interface IAudioChannelDefinition
    {
        string Id { get; }
        string OutputGroupId { get; }
        bool Loop { get; }
        float Volume { get; }

        /// <summary>The most playbacks (one-shots and looping effects) the channel plays at once. Longer
        /// lasting music and ambience tracks do not count against it.</summary>
        int MaxOneShotCount { get; }
    }

    public sealed class AudioChannelDefinition : IAudioChannelDefinition
    {
        public string Id { get; }
        public string OutputGroupId { get; }
        public bool Loop { get; }
        public float Volume { get; }
        public int MaxOneShotCount { get; }

        public AudioChannelId ChannelId => new(Id);

        public AudioChannelDefinition(
            string id,
            bool loop = false,
            float volume = 1f,
            int maxOneShotCount = 8)
            : this(id, null, loop, volume, maxOneShotCount)
        {
        }

        public AudioChannelDefinition(
            string id,
            string outputGroupId,
            bool loop = false,
            float volume = 1f,
            int maxOneShotCount = 8)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Audio channel id is required.", nameof(id)) : id;
            if (maxOneShotCount < 0)
                throw new ArgumentOutOfRangeException(nameof(maxOneShotCount));

            OutputGroupId = outputGroupId;
            Loop = loop;
            Volume = volume;
            MaxOneShotCount = maxOneShotCount;
        }
    }
}
