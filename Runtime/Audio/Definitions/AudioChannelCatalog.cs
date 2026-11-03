using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Foundation.Audio
{
    [Serializable]
    public sealed class AudioChannelEntry
    {
        [SerializeField] private AudioChannelId id;
        [SerializeField] private string displayName;
        [SerializeField] private string outputGroupId;
        [SerializeField] private bool loop;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [SerializeField, Min(0), FormerlySerializedAs("maxVoices")] private int maxPlaybacks = 8;

        public AudioChannelId Id => id;

        public string DisplayName => string.IsNullOrEmpty(displayName) 
            ? id.Value 
            : displayName;

        public IAudioChannelDefinition ToDefinition()
            => new AudioChannelDefinition(id.Value, string.IsNullOrEmpty(outputGroupId) 
                ? null 
                : outputGroupId, 
                loop, volume, maxPlaybacks);
    }

    /// <summary>
    /// The channels (buses) of a project as an asset, so they appear in the audio editor's channel picker.
    /// Optional: channels can be built in code.
    /// </summary>
    [CreateAssetMenu(menuName = "Foundation/Audio/Channel Catalog", fileName = "AudioChannelCatalog")]
    public sealed class AudioChannelCatalog : ScriptableObject
    {
        [SerializeField] private List<AudioChannelEntry> channels = new();

        public IReadOnlyList<AudioChannelEntry> Channels => channels;

        public IAudioChannelDefinition[] ToDefinitions()
        {
            var result = new IAudioChannelDefinition[channels.Count];
            for (var index = 0; index < result.Length; index++)
                result[index] = channels[index].ToDefinition();

            return result;
        }
    }
}
