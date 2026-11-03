using System;
using System.Collections.Generic;
using Foundation.Pooling;
using UnityEngine;
using UnityEngine.Audio;
using Object = UnityEngine.Object;

namespace Foundation.Audio
{
    /// <summary>The pools the audio backend uses; their ids show up in the pool diagnostics.</summary>
    public static class AudioPoolIds
    {
        public static readonly PoolId Playback2D = PoolId.From("audio.playback.2d");
        public static readonly PoolId Playback3D = PoolId.From("audio.playback.3d");
        public static readonly PoolId PlaybackLoop = PoolId.From("audio.playback.loop");
    }

    /// <summary>
    /// Plays one-shot sounds through pooled <c>AudioSource</c>s and tracks through persistent ones. Three pools
    /// because lifecycles differ: short, numerous 2D one-shots; positioned 3D sounds with spatial setup;
    /// loops that must never starve the one-shots.
    /// No AudioSource is created or destroyed per play.
    /// </summary>
    public sealed class UnityAudioBackend : IAudioBackend, IDisposable
    {
        private readonly IPoolService _poolService;
        private readonly IAudioMixerRouting _routing;
        private readonly Transform _root;
        private Pool<PlaybackSource> _pool2D;
        private Pool<PlaybackSource> _pool3D;
        private Pool<PlaybackSource> _poolLoop;
        private PlaybackSource[] _playbacks;
        private Pool<PlaybackSource>[] _playbackPools;
        private AudioMixerGroup[] _groups;
        private AudioSource[] _trackSources;
        private int _followCount;
        private bool _disposed;

        public Transform PlaybackRoot => _root;

        public UnityAudioBackend(IPoolService poolService, IAudioMixerRouting routing)
        {
            _poolService = poolService ?? throw new ArgumentNullException(nameof(poolService));
            _routing = routing ?? NullAudioMixerRouting.Instance;
            var root = new GameObject("[Audio]");
            if (Application.isPlaying)
                Object.DontDestroyOnLoad(root);

            _root = root.transform;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _poolService.Release(AudioPoolIds.Playback2D);
            _poolService.Release(AudioPoolIds.Playback3D);
            _poolService.Release(AudioPoolIds.PlaybackLoop);
            if (_root != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_root.gameObject);
                else
                    Object.DestroyImmediate(_root.gameObject);
            }
        }

        public void Configure(IReadOnlyList<IAudioChannelDefinition> channels, int maxPlaybacks)
        {
            _playbacks = new PlaybackSource[maxPlaybacks];
            _playbackPools = new Pool<PlaybackSource>[maxPlaybacks];
            _groups = new AudioMixerGroup[channels.Count];
            for (var index = 0; index < channels.Count; index++)
                _groups[index] = _routing.Resolve(new AudioChannelId(channels[index].Id));

            _trackSources = new AudioSource[channels.Count * 2];
            _pool2D = _poolService.CreatePool(AudioPoolIds.Playback2D, new PlaybackSourceStrategy(_root, "Playback2D"), new PoolSettings(prewarmCount: Math.Min(8, maxPlaybacks), maxRetained: maxPlaybacks));
            _pool3D = _poolService.CreatePool(AudioPoolIds.Playback3D, new PlaybackSourceStrategy(_root, "Playback3D"), new PoolSettings(prewarmCount: Math.Min(4, maxPlaybacks), maxRetained: maxPlaybacks));
            _poolLoop = _poolService.CreatePool(AudioPoolIds.PlaybackLoop, new PlaybackSourceStrategy(_root, "PlaybackLoop"), new PoolSettings(prewarmCount: 2, maxRetained: maxPlaybacks));
            _pool2D.Prewarm(_pool2D.Settings.PrewarmCount);
            _pool3D.Prewarm(_pool3D.Settings.PrewarmCount);
            _poolLoop.Prewarm(_poolLoop.Settings.PrewarmCount);
        }

        public bool StartPlayback(int slot, in AudioPlaybackStart start)
        {
            var pool = start.Loop ? _poolLoop : (start.IsSpatial ? _pool3D : _pool2D);
            if (!pool.TryRent(out var playback))
                return false;

            _playbacks[slot] = playback;
            _playbackPools[slot] = pool;
            var source = playback.Source;
            source.clip = start.Clip;
            source.volume = start.Volume;
            source.pitch = start.Pitch;
            source.loop = start.Loop;
            source.outputAudioMixerGroup = _groups[start.ChannelIndex];
            if (start.IsSpatial)
            {
                source.spatialBlend = start.SpatialBlend;
                source.minDistance = start.MinDistance;
                source.maxDistance = start.MaxDistance;
                source.rolloffMode = start.Rolloff;
                playback.Transform.position = start.Position;
                playback.Follow = start.Follow;
                if (start.Follow != null)
                    _followCount++;
            }
            else
            {
                source.spatialBlend = 0f;
            }

            source.Play();
            return true;
        }

        public void StopPlayback(int slot)
        {
            var playback = _playbacks[slot];
            if (playback == null)
                return;

            if (playback.Follow != null)
                _followCount--;

            playback.Follow = null;
            _playbacks[slot] = null;
            _playbackPools[slot].Return(playback);
            _playbackPools[slot] = null;
        }

        public void SetPlaybackVolume(int slot, float volume)
        {
            var playback = _playbacks[slot];
            if (playback != null)
                playback.Source.volume = volume;
        }

        public void SetPlaybackPitch(int slot, float pitch)
        {
            var playback = _playbacks[slot];
            if (playback != null)
                playback.Source.pitch = pitch;
        }

        public bool IsSlotPlaying(int slot)
        {
            var playback = _playbacks[slot];
            return playback != null && playback.Source.isPlaying;
        }

        public void StartTrack(int channelIndex, int slot, AudioClip clip, float volume, float pitch, bool loop)
        {
            var source = GetTrackSource(channelIndex, slot);
            source.clip = clip;
            source.volume = volume;
            source.pitch = pitch;
            source.loop = loop;
            source.Play();
        }

        public void StopTrack(int channelIndex, int slot)
        {
            var source = _trackSources[(channelIndex * 2) + slot];
            if (source == null)
                return;

            source.Stop();
            source.clip = null;
        }

        public void SetTrackVolume(int channelIndex, int slot, float volume)
        {
            var source = _trackSources[(channelIndex * 2) + slot];
            if (source != null)
                source.volume = volume;
        }

        public bool IsTrackPlaying(int channelIndex, int slot)
        {
            var source = _trackSources[(channelIndex * 2) + slot];
            return source != null && source.isPlaying;
        }

        public void SetMasterVolume(float volume) => AudioListener.volume = volume;

        public void Tick()
        {
            if (_followCount == 0)
                return;

            foreach (var playback in _playbacks)
            {
                if (playback != null && playback.Follow != null)
                    playback.Transform.position = playback.Follow.position;
            }
        }

        private AudioSource GetTrackSource(int channelIndex, int slot)
        {
            var index = (channelIndex * 2) + slot;
            var source = _trackSources[index];
            if (source != null) 
                return source;
            var gameObject = new GameObject($"Track {channelIndex}.{slot}");
            gameObject.transform.SetParent(_root, false);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = _groups[channelIndex];
            _trackSources[index] = source;

            return source;
        }

        private sealed class PlaybackSource
        {
            public GameObject GameObject;
            public Transform Transform;
            public AudioSource Source;
            public Transform Follow;
        }

        private sealed class PlaybackSourceStrategy : IPoolStrategy<PlaybackSource>
        {
            private readonly Transform _parent;
            private readonly string _name;

            public PlaybackSourceStrategy(Transform parent, string name)
            {
                _parent = parent;
                _name = name;
            }

            public PlaybackSource Create()
            {
                var gameObject = new GameObject(_name);
                gameObject.transform.SetParent(_parent, false);
                var source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                gameObject.SetActive(false);
                return new PlaybackSource { GameObject = gameObject, Transform = gameObject.transform, Source = source };
            }

            public bool IsValid(PlaybackSource item) => item.GameObject != null;

            public void OnRent(PlaybackSource item) => item.GameObject.SetActive(true);

            public void OnReturn(PlaybackSource item)
            {
                item.Source.Stop();
                item.Source.clip = null;
                item.Source.outputAudioMixerGroup = null;
                item.Transform.position = Vector3.zero;
                item.GameObject.SetActive(false);
            }

            public void OnDestroy(PlaybackSource item)
            {
                if (item.GameObject == null)
                    return;

                if (Application.isPlaying)
                    Object.Destroy(item.GameObject);
                else
                    Object.DestroyImmediate(item.GameObject);
            }
        }
    }
}
