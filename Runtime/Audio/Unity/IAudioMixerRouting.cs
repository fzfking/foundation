using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Foundation.Audio
{
    public interface IAudioMixerRouting
    {
        /// <summary>The group for the channel, or null to play straight to the master output.</summary>
        AudioMixerGroup Resolve(AudioChannelId channel);
    }

    public sealed class NullAudioMixerRouting : IAudioMixerRouting
    {
        public static readonly NullAudioMixerRouting Instance = new();

        public AudioMixerGroup Resolve(AudioChannelId channel) => null;
    }

    /// <summary>
    /// Routes channels through an AudioMixer authored in the project, so effects, snapshots and exposed
    /// parameters stay in Unity's mixer.
    /// </summary>
    [CreateAssetMenu(menuName = "Foundation/Audio/Mixer Routing", fileName = "AudioMixerRouting")]
    public sealed class AudioMixerRouting : ScriptableObject, IAudioMixerRouting
    {
        [SerializeField] private List<Route> routes = new();

        public AudioMixerGroup Resolve(AudioChannelId channel)
        {
            for (var index = 0; index < routes.Count; index++)
            {
                if (routes[index].channel == channel)
                    return routes[index].group;
            }

            return null;
        }

        [Serializable]
        private struct Route
        {
            public AudioChannelId channel;
            public AudioMixerGroup group;
        }
    }
}
