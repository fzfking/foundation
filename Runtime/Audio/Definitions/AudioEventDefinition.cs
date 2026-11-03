using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Foundation.Audio
{
    /// <summary>3D positioning of an event. Disabled means a plain 2D sound.</summary>
    [Serializable]
    public struct AudioSpatialSettings
    {
        [SerializeField] private bool enabled;
        [SerializeField, Range(0f, 1f)] private float spatialBlend;
        [SerializeField, Min(0f)] private float minDistance;
        [SerializeField, Min(0.01f)] private float maxDistance;
        [SerializeField] private AudioRolloffMode rolloff;

        public bool Enabled => enabled;

        public float SpatialBlend => enabled ? (spatialBlend <= 0f ? 1f : spatialBlend) : 0f;

        public float MinDistance => minDistance <= 0f ? 1f : minDistance;

        public float MaxDistance => maxDistance <= 0f ? 50f : maxDistance;

        public AudioRolloffMode Rolloff => rolloff;

        public AudioSpatialSettings(bool enabled, float spatialBlend = 1f, float minDistance = 1f, float maxDistance = 50f, AudioRolloffMode rolloff = AudioRolloffMode.Logarithmic)
        {
            this.enabled = enabled;
            this.spatialBlend = spatialBlend;
            this.minDistance = minDistance;
            this.maxDistance = maxDistance;
            this.rolloff = rolloff;
        }
    }

    /// <summary>
    /// What a sound means to the game. Gameplay refers to the <see cref="Id"/>; how it sounds (clip,
    /// loudness, frequency) lives here and is tuned without touching code.
    /// </summary>
    [Serializable]
    public sealed class AudioEventDefinition
    {
        [SerializeField] private AudioEventId id;
        [SerializeField] private string displayName;
        [SerializeField] private string category;
        [SerializeField] private AudioChannelId channel;
        [SerializeField] private AudioClipEntry[] clips = Array.Empty<AudioClipEntry>();
        [SerializeField] private AudioSelectionMode selection = AudioSelectionMode.Single;
        [SerializeField] private Vector2 volumeRange = Vector2.one;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [SerializeField, Min(0f)] private float cooldownSeconds;
        [SerializeField, Min(0), FormerlySerializedAs("maxVoices")] private int maxPlaybacks;
        [SerializeField] private AudioPlaybackOverflowPolicy overflowPolicy;
        [SerializeField, Range(0, 255)] private int priority = 128;
        [SerializeField] private bool loop;
        [SerializeField] private AudioSpatialSettings spatial;

        public AudioEventId Id => id;

        /// <summary>The name designers see; falls back to the id. May change freely.</summary>
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id.Value : displayName;

        /// <summary>Slash separated path that groups events in the editor, for example <c>UI/Buttons</c>.</summary>
        public string Category => category;

        public AudioChannelId Channel => channel;

        public AudioClipEntry[] Clips => clips;

        public AudioSelectionMode Selection => selection;

        /// <summary>Each play picks a random volume in this range (x = min, y = max).</summary>
        public Vector2 VolumeRange => volumeRange;

        /// <summary>Each play picks a random pitch in this range (x = min, y = max).</summary>
        public Vector2 PitchRange => pitchRange;

        /// <summary>Plays requested sooner than this after the last started play of the event are ignored. 0 = off.</summary>
        public float CooldownSeconds => cooldownSeconds;

        /// <summary>The most simultaneous playbacks of this event. 0 = unlimited.</summary>
        public int MaxPlaybacks => maxPlaybacks;

        public AudioPlaybackOverflowPolicy OverflowPolicy => overflowPolicy;

        /// <summary>When a channel or the engine runs out of playbacks, lower priority sounds are stolen first. Higher = more important.</summary>
        public int Priority => priority;

        public bool Loop => loop;

        public AudioSpatialSettings Spatial => spatial;

        public AudioEventDefinition()
        {
        }

        public AudioEventDefinition(
            AudioEventId id,
            AudioChannelId channel,
            AudioClipEntry[] clips,
            string displayName = null,
            string category = null,
            AudioSelectionMode selection = AudioSelectionMode.Single,
            float volumeMin = 1f,
            float volumeMax = 1f,
            float pitchMin = 1f,
            float pitchMax = 1f,
            float cooldownSeconds = 0f,
            int maxPlaybacks = 0,
            AudioPlaybackOverflowPolicy overflowPolicy = AudioPlaybackOverflowPolicy.IgnoreNew,
            int priority = 128,
            bool loop = false,
            AudioSpatialSettings spatial = default)
        {
            this.id = id;
            this.channel = channel;
            this.clips = clips ?? Array.Empty<AudioClipEntry>();
            this.displayName = displayName;
            this.category = category;
            this.selection = selection;
            volumeRange = new(volumeMin, volumeMax);
            pitchRange = new(pitchMin, pitchMax);
            this.cooldownSeconds = cooldownSeconds;
            this.maxPlaybacks = maxPlaybacks;
            this.overflowPolicy = overflowPolicy;
            this.priority = priority;
            this.loop = loop;
            this.spatial = spatial;
        }
    }
}
