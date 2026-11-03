using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Audio
{
    public struct AudioPlaybackStart
    {
        public AudioClip Clip;
        public int ChannelIndex;
        public float Volume;
        public float Pitch;
        public bool Loop;
        public bool IsSpatial;
        public Vector3 Position;
        public Transform Follow;
        public float SpatialBlend;
        public float MinDistance;
        public float MaxDistance;
        public AudioRolloffMode Rolloff;
    }

    /// <summary>
    /// The sound output. The engine owns every decision and tells the backend what to do per playback slot, so
    /// the Unity implementation stays a thin pooled <c>AudioSource</c> driver and tests can run the engine
    /// against a fake.
    /// </summary>
    public interface IAudioBackend
    {
        void Configure(IReadOnlyList<IAudioChannelDefinition> channels, int maxPlaybacks);
        bool StartPlayback(int slot, in AudioPlaybackStart start);
        void StopPlayback(int slot);
        void SetPlaybackVolume(int slot, float volume);
        void SetPlaybackPitch(int slot, float pitch);
        bool IsSlotPlaying(int slot);
        void StartTrack(int channelIndex, int slot, AudioClip clip, float volume, float pitch, bool loop);
        void StopTrack(int channelIndex, int slot);
        void SetTrackVolume(int channelIndex, int slot, float volume);
        bool IsTrackPlaying(int channelIndex, int slot);
        void SetMasterVolume(float volume);
        void Tick();
    }

    public sealed class NullAudioBackend : IAudioBackend
    {
        public void Configure(IReadOnlyList<IAudioChannelDefinition> channels, int maxPlaybacks)
        {
        }

        public bool StartPlayback(int slot, in AudioPlaybackStart start) => true;

        public void StopPlayback(int slot)
        {
        }

        public void SetPlaybackVolume(int slot, float volume)
        {
        }

        public void SetPlaybackPitch(int slot, float pitch)
        {
        }

        public bool IsSlotPlaying(int slot) => false;

        public void StartTrack(int channelIndex, int slot, AudioClip clip, float volume, float pitch, bool loop)
        {
        }

        public void StopTrack(int channelIndex, int slot)
        {
        }

        public void SetTrackVolume(int channelIndex, int slot, float volume)
        {
        }

        public bool IsTrackPlaying(int channelIndex, int slot) => false;

        public void SetMasterVolume(float volume)
        {
        }

        public void Tick()
        {
        }
    }
}
