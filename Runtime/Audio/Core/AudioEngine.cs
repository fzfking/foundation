using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Randomization;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Audio
{
    public sealed class AudioEngineSettings
    {
        /// <summary>Playback slots shared by every channel. A new sound steals a lower priority one when all are busy.</summary>
        public int MaxPlaybacks { get; set; } = 32;
    }

    /// <summary>
    /// The decision-making core of audio. For every request it applies the event rules (clip selection,
    /// cooldown, playback limits, priority stealing) and the channel rules (volume, mute, ducking, fades), then
    /// tells the <see cref="IAudioBackend"/> what to play.
    /// It owns no Unity objects and allocates nothing per play. Main thread only; call <see cref="Tick"/>
    /// once per frame.
    /// </summary>
    public sealed class AudioEngine : IDisposable
    {
        private const double EndMargin = 0.05d;
        private const float MinPitch = 0.01f;
        private const float VolumeEpsilon = 0.0001f;

        private readonly IAudioBackend _backend;
        private readonly IAudioSettings _settings;
        private readonly IAudioClipResolver _clips;
        private readonly IRandomService _random;
        private readonly IAudioClock _clock;
        private readonly ChannelState[] _channels;
        private readonly Dictionary<AudioChannelId, int> _channelIndex;
        private readonly Playback[] _playbacks;
        private readonly bool[] _channelChanged;

        private bool _disposed;
        private double _lastTick;
        private int _activePlaybacks;
        private int _activeTracks;
        private double _windowStart;
        private int _requestsInWindow;
        private int _requestsLastWindow;
        private long _totalRequests;
        private long _totalStarted;
        private long _playbackRejects;
        private long _cooldownRejects;
        private long _clipNotLoadedRejects;
        private long _steals;
        private (ChannelState trigger, ChannelState target, float gain)[] _duckRules = Array.Empty<(ChannelState, ChannelState, float)>();

        public AudioEventRegistry Events { get; }

        public int ChannelCount => _channels.Length;

        public AudioEngine(
            IAudioBackend backend,
            IReadOnlyList<IAudioChannelDefinition> channels,
            IAudioSettings settings,
            AudioEventRegistry events,
            IAudioClipResolver clips = null,
            IRandomService random = null,
            IAudioClock clock = null,
            IReadOnlyList<AudioDuckingRule> duckingRules = null,
            AudioEngineSettings engineSettings = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Events = events ?? throw new ArgumentNullException(nameof(events));
            _clips = clips ?? new DirectAudioClipResolver();
            _random = random ?? new SystemRandomService();
            _clock = clock ?? UnscaledAudioClock.Instance;
            var maxPlaybacks = Math.Max(1, (engineSettings ?? new AudioEngineSettings()).MaxPlaybacks);
            _playbacks = new Playback[maxPlaybacks];
            for (var index = 0; index < maxPlaybacks; index++)
                _playbacks[index].Generation = 1;

            channels ??= Array.Empty<IAudioChannelDefinition>();
            _channels = new ChannelState[channels.Count];
            _channelChanged = new bool[channels.Count];
            _channelIndex = new(channels.Count);
            for (var index = 0; index < channels.Count; index++)
            {
                var state = new ChannelState(channels[index], index);
                if (!_channelIndex.TryAdd(state.Id, index))
                    throw new InvalidOperationException($"Audio channel '{state.Id.Value}' is registered more than once.");

                _channels[index] = state;
            }

            ConfigureDucking(duckingRules);
            _backend.Configure(channels, maxPlaybacks);
            RecomputeBaseGains();
            _lastTick = _clock.Now;
            _windowStart = _lastTick;
            _settings.Changed += OnSettingsChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AudioDiagnostics.Current = this;
#endif
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            StopAll();
            _disposed = true;
            _settings.Changed -= OnSettingsChanged;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (AudioDiagnostics.Current == this)
                AudioDiagnostics.Current = null;
#endif
        }

        #region Playing events

        public AudioHandle Play(AudioEventId id)
        {
            TryPlay(id, default, out var handle);
            return handle;
        }

        public AudioHandle Play(AudioEventId id, in AudioPlayOptions options)
        {
            TryPlay(id, options, out var handle);
            return handle;
        }
        
        public AudioPlayResult TryPlay(AudioEventId id, in AudioPlayOptions options, out AudioHandle handle)
        {
            handle = default;
            if (_disposed)
                return AudioPlayResult.Disposed;

            _requestsInWindow++;
            _totalRequests++;
            if (!Events.TryGetRuntime(id, out var ev))
                return AudioPlayResult.UnknownEvent;

            var definition = ev.Definition;
            if (!TryResolveChannel(ev, out var channel))
                return AudioPlayResult.UnknownChannel;

            var now = _clock.Now;
            if (definition.CooldownSeconds > 0f && now - ev.LastPlayTime < definition.CooldownSeconds)
            {
                _cooldownRejects++;
                return AudioPlayResult.Cooldown;
            }

            if (channel.BaseGain <= 0f)
                return AudioPlayResult.Silenced;

            var clipIndex = AudioClipSelector.Pick(definition.Selection, definition.Clips, ev.LastClipIndex, _random);
            if (clipIndex < 0)
                return AudioPlayResult.NoClips;

            if (!_clips.TryGetClip(in definition.Clips[clipIndex], out var clip))
            {
                _clipNotLoadedRejects++;
                return AudioPlayResult.ClipNotLoaded;
            }

            var volume = RandomRange(definition.VolumeRange) * options.VolumeScale;
            var pitch = RandomRange(definition.PitchRange) * options.PitchScale;
            var result = StartPlayback(channel, ev, definition.Priority, clip, volume, pitch, definition.Loop, definition.Spatial, in options, null, out handle);
            if (result == AudioPlayResult.Started)
            {
                ev.LastPlayTime = now;
                ev.LastClipIndex = clipIndex;
            }

            return result;
        }

        /// <summary>
        /// Plays a clip that is not part of an event (loaded by id through the string API). The engine owns
        /// <paramref name="lease"/> and disposes it when the playback ends, or at once if the sound is rejected.
        /// </summary>
        public AudioPlayResult TryPlayClip(AudioClip clip, AudioChannelId channelId, IDisposable lease, out AudioHandle handle)
        {
            handle = default;
            if (_disposed)
            {
                lease?.Dispose();
                return AudioPlayResult.Disposed;
            }

            _requestsInWindow++;
            _totalRequests++;
            if (!_channelIndex.TryGetValue(channelId, out var index))
            {
                lease?.Dispose();
                return AudioPlayResult.UnknownChannel;
            }

            var channel = _channels[index];
            if (channel.BaseGain <= 0f)
            {
                lease?.Dispose();
                return AudioPlayResult.Silenced;
            }

            return StartPlayback(channel, null, 128, clip, 1f, 1f, false, default, default, lease, out handle);
        }

        public void StopEvent(AudioEventId id, float fadeSeconds = 0f)
        {
            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                if (_playbacks[slot].Active && _playbacks[slot].Event != null && _playbacks[slot].Event.Definition.Id == id)
                    StopPlaybackAt(slot, fadeSeconds);
            }
        }

        public void StopChannel(AudioChannelId channelId, float fadeSeconds = 0f)
        {
            if (!_channelIndex.TryGetValue(channelId, out var index))
                return;

            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                if (_playbacks[slot].Active && _playbacks[slot].Channel == _channels[index])
                    StopPlaybackAt(slot, fadeSeconds);
            }

            StopTrackInternal(_channels[index], fadeSeconds);
        }

        public void StopAll()
        {
            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                if (_playbacks[slot].Active)
                    ReleasePlayback(slot);
            }

            for (var index = 0; index < _channels.Length; index++)
                StopTrackInternal(_channels[index], 0f);
        }

        #endregion

        #region Playback handle operations

        internal bool IsPlaying(int slot, int generation) => IsLive(slot, generation);

        internal void StopPlayback(int slot, int generation, float fadeSeconds)
        {
            if (IsLive(slot, generation))
                StopPlaybackAt(slot, fadeSeconds);
        }

        internal void SetPlaybackVolume(int slot, int generation, float volume)
        {
            if (!IsLive(slot, generation))
                return;

            _playbacks[slot].HandleVolume = Mathf.Clamp01(volume);
            ApplyPlaybackVolume(slot);
        }

        internal void SetPlaybackPitch(int slot, int generation, float pitch)
        {
            if (!IsLive(slot, generation))
                return;

            _playbacks[slot].Pitch = Mathf.Max(MinPitch, pitch);
            _backend.SetPlaybackPitch(slot, _playbacks[slot].Pitch);
        }

        #endregion

        #region Tracks (music and ambience)

        public AudioPlayResult PlayMusic(AudioEventId id, float fadeInSeconds = 0f) => PlayTrackEvent(id, fadeInSeconds, crossFade: false);

        /// <summary>Plays an event as the channel's track, fading it in while the current one fades out.</summary>
        public AudioPlayResult CrossFadeMusic(AudioEventId id, float durationSeconds) => PlayTrackEvent(id, durationSeconds, crossFade: true);

        public void StopMusic(AudioChannelId channelId, float fadeOutSeconds = 0f)
        {
            if (_channelIndex.TryGetValue(channelId, out var index))
                StopTrackInternal(_channels[index], fadeOutSeconds);
        }

        /// <summary>Starts a track from a clip. The engine owns <paramref name="lease"/> and disposes it when the track stops.</summary>
        public void PlayTrack(AudioChannelId channelId, AudioClip clip, string key, IDisposable lease, bool loop, float fadeInSeconds = 0f, float volume = 1f, float pitch = 1f)
        {
            var channel = RequireChannel(channelId);
            ReleaseAllTrackSlots(channel);
            BeginTrack(channel, 0, clip, key, lease, loop, volume, pitch, fadeInSeconds);
        }

        public void CrossFadeTrack(AudioChannelId channelId, AudioClip clip, string key, IDisposable lease, bool loop, float durationSeconds, float volume = 1f, float pitch = 1f)
        {
            var channel = RequireChannel(channelId);
            if (durationSeconds <= 0f || channel.CurrentSlot < 0)
            {
                ReleaseAllTrackSlots(channel);
                BeginTrack(channel, 0, clip, key, lease, loop, volume, pitch, 0f);
                return;
            }

            var outgoing = channel.CurrentSlot;
            var incoming = 1 - outgoing;
            ReleaseTrackSlot(channel, incoming); // a previous cross-fade still fading out is cut short
            StartTrackFade(ref channel.Slots[outgoing], 0f, durationSeconds, stopAtEnd: true);
            BeginTrack(channel, incoming, clip, key, lease, loop, volume, pitch, durationSeconds);
        }

        public void StopTrack(AudioChannelId channelId, float fadeOutSeconds = 0f) => StopTrackInternal(RequireChannel(channelId), fadeOutSeconds);

        /// <summary>Stops, at once, every track that was started with <paramref name="key"/>.</summary>
        public void StopTracks(string key)
        {
            ThrowIfDisposed();
            for (var index = 0; index < _channels.Length; index++)
            {
                var channel = _channels[index];
                for (var slot = 0; slot < channel.Slots.Length; slot++)
                {
                    if (channel.Slots[slot].Active && channel.Slots[slot].Key == key)
                        ReleaseTrackSlot(channel, slot);
                }
            }
        }

        public bool IsTrackPlaying(AudioChannelId channelId) => RequireChannel(channelId).CurrentSlot >= 0;

        /// <summary>The key of the channel's current track, or null.</summary>
        public string GetTrackKey(AudioChannelId channelId)
        {
            var channel = RequireChannel(channelId);
            return channel.CurrentSlot >= 0 ? channel.Slots[channel.CurrentSlot].Key : null;
        }

        /// <summary>Ramps the channel's temporary fade multiplier (separate from the player's volume setting).</summary>
        public void FadeChannel(AudioChannelId channelId, float targetGain, float durationSeconds)
        {
            var channel = RequireChannel(channelId);
            targetGain = Mathf.Clamp01(targetGain);
            if (durationSeconds <= 0f)
            {
                channel.UserFade = targetGain;
                channel.UserFadeDuration = 0f;
                return;
            }

            channel.UserFadeFrom = channel.UserFade;
            channel.UserFadeTo = targetGain;
            channel.UserFadeStart = _clock.Now;
            channel.UserFadeDuration = durationSeconds;
        }

        public bool TryGetChannel(string channelId, out IAudioChannelDefinition definition)
        {
            if (!string.IsNullOrEmpty(channelId) && _channelIndex.TryGetValue(new AudioChannelId(channelId), out var index))
            {
                definition = _channels[index].Definition;
                return true;
            }

            definition = null;
            return false;
        }

        #endregion

        public UniTask PreloadAsync(IReadOnlyList<AudioEventId> ids, CancellationToken cancellationToken = default)
        {
            var entries = new List<AudioClipEntry>();
            for (var index = 0; index < ids.Count; index++)
            {
                if (!Events.TryGetDefinition(ids[index], out var definition))
                    continue;

                for (var clip = 0; clip < definition.Clips.Length; clip++)
                {
                    if (!definition.Clips[clip].IsEmpty)
                        entries.Add(definition.Clips[clip]);
                }
            }

            return _clips.PreloadAsync(entries, cancellationToken);
        }

        /// <summary>Drives fades, ducking and playback completion. Call once per frame.</summary>
        public void Tick()
        {
            if (_disposed)
                return;

            var now = _clock.Now;
            var deltaTime = (float)Math.Min(now - _lastTick, 0.25d);
            _lastTick = now;
            UpdateDucking(deltaTime);
            for (var index = 0; index < _channels.Length; index++)
            {
                var channel = _channels[index];
                UpdateChannelFade(channel, now);
                var gain = channel.BaseGain * channel.DuckGain * channel.UserFade;
                _channelChanged[index] = Math.Abs(gain - channel.AppliedGain) > VolumeEpsilon;
                channel.AppliedGain = gain;
            }

            TickPlaybacks(now);
            TickTracks(now);
            _backend.Tick();
            if (!(now - _windowStart >= 1d)) 
                return;
            _requestsLastWindow = _requestsInWindow;
            _requestsInWindow = 0;
            _windowStart = now;
        }

        public AudioStats GetStats() => new(
            _activePlaybacks, _activeTracks, _playbacks.Length, _requestsLastWindow, _totalRequests,
            _totalStarted, _playbackRejects, _cooldownRejects, _clipNotLoadedRejects, _steals);

        public void CollectChannels(List<AudioChannelSnapshot> results)
        {
            for (var index = 0; index < _channels.Length; index++)
            {
                var channel = _channels[index];
                results.Add(new AudioChannelSnapshot(
                    channel.Id, channel.AppliedGain, _settings.IsChannelMuted(channel.Definition.Id),
                    channel.ActivePlaybacks, channel.MaxPlaybacks, channel.DuckGain, channel.CurrentSlot >= 0));
            }
        }

        private AudioPlayResult StartPlayback(
            ChannelState channel,
            AudioEventRuntime ev,
            int priority,
            AudioClip clip,
            float volume,
            float pitch,
            bool loop,
            in AudioSpatialSettings spatial,
            in AudioPlayOptions options,
            IDisposable lease,
            out AudioHandle handle)
        {
            handle = default;
            pitch = Mathf.Max(MinPitch, pitch);
            
            var eventVictim = -1;
            if (ev != null && ev.Definition.MaxPlaybacks > 0 && ev.ActivePlaybacks >= ev.Definition.MaxPlaybacks)
            {
                if (ev.Definition.OverflowPolicy == AudioPlaybackOverflowPolicy.IgnoreNew)
                    return Reject(AudioPlayResult.EventPlaybackLimit, lease);

                eventVictim = FindOldestPlaybackOf(ev);
            }

            var channelCount = channel.ActivePlaybacks - (eventVictim >= 0 && _playbacks[eventVictim].Channel == channel ? 1 : 0);
            var channelVictim = -1;
            if (channelCount >= channel.MaxPlaybacks)
            {
                channelVictim = FindStealCandidate(channel, priority, eventVictim, -1);
                if (channelVictim < 0)
                    return Reject(AudioPlayResult.ChannelFull, lease);
            }

            var totalCount = _activePlaybacks - (eventVictim >= 0 ? 1 : 0) - (channelVictim >= 0 ? 1 : 0);
            var globalVictim = -1;
            if (totalCount >= _playbacks.Length)
            {
                globalVictim = FindStealCandidate(null, priority, eventVictim, channelVictim);
                if (globalVictim < 0)
                    return Reject(AudioPlayResult.Saturated, lease);
            }

            var slot = -1;
            var victims = 0;
            if (eventVictim >= 0)
            {
                ReleasePlayback(eventVictim);
                victims++;
            }

            if (channelVictim >= 0)
            {
                ReleasePlayback(channelVictim);
                victims++;
            }

            if (globalVictim >= 0)
            {
                ReleasePlayback(globalVictim);
                victims++;
            }

            _steals += victims;
            for (var index = 0; index < _playbacks.Length; index++)
            {
                if (!_playbacks[index].Active)
                {
                    slot = index;
                    break;
                }
            }

            if (slot < 0)
                return Reject(AudioPlayResult.Saturated, lease);

            var now = _clock.Now;
            ref var playback = ref _playbacks[slot];
            playback.Event = ev;
            playback.Channel = channel;
            playback.Priority = priority;
            playback.StartTime = now;
            playback.ExpectedEnd = now + (clip.length / Math.Abs(pitch)) + EndMargin;
            playback.EventVolume = Mathf.Clamp01(volume);
            playback.HandleVolume = 1f;
            playback.Pitch = pitch;
            playback.Loop = loop;
            playback.Lease = lease;
            playback.Gain = 1f;
            playback.FadeDuration = 0f;
            playback.StopAtFadeEnd = false;

            var start = new AudioPlaybackStart
            {
                Clip = clip,
                ChannelIndex = channel.Index,
                Volume = ComputePlaybackVolume(in playback),
                Pitch = pitch,
                Loop = loop,
                IsSpatial = spatial.Enabled && options.HasPosition,
                Position = options.Position,
                Follow = options.Follow,
                SpatialBlend = spatial.SpatialBlend,
                MinDistance = spatial.MinDistance,
                MaxDistance = spatial.MaxDistance,
                Rolloff = spatial.Rolloff,
            };
            if (!_backend.StartPlayback(slot, in start))
            {
                playback.Event = null;
                playback.Channel = null;
                playback.Lease = null;
                lease?.Dispose();
                return AudioPlayResult.BackendRejected;
            }

            playback.Active = true;
            channel.ActivePlaybacks++;
            if (ev != null)
                ev.ActivePlaybacks++;

            _activePlaybacks++;
            _totalStarted++;
            handle = new AudioHandle(this, slot, playback.Generation);
            return AudioPlayResult.Started;
        }

        private AudioPlayResult Reject(AudioPlayResult reason, IDisposable lease)
        {
            _playbackRejects++;
            lease?.Dispose();
            return reason;
        }

        private AudioPlayResult PlayTrackEvent(AudioEventId id, float seconds, bool crossFade)
        {
            if (_disposed)
                return AudioPlayResult.Disposed;

            _requestsInWindow++;
            _totalRequests++;
            if (!Events.TryGetRuntime(id, out var ev))
                return AudioPlayResult.UnknownEvent;

            if (!TryResolveChannel(ev, out var channel))
                return AudioPlayResult.UnknownChannel;

            var definition = ev.Definition;
            if (channel.BaseGain <= 0f)
                return AudioPlayResult.Silenced;

            var clipIndex = AudioClipSelector.Pick(definition.Selection, definition.Clips, ev.LastClipIndex, _random);
            if (clipIndex < 0)
                return AudioPlayResult.NoClips;

            if (!_clips.TryGetClip(in definition.Clips[clipIndex], out var clip))
            {
                _clipNotLoadedRejects++;
                return AudioPlayResult.ClipNotLoaded;
            }

            var volume = RandomRange(definition.VolumeRange);
            var pitch = Mathf.Max(MinPitch, RandomRange(definition.PitchRange));
            if (crossFade)
                CrossFadeTrack(channel.Id, clip, id.Value, null, definition.Loop, seconds, volume, pitch);
            else
                PlayTrack(channel.Id, clip, id.Value, null, definition.Loop, seconds, volume, pitch);

            ev.LastPlayTime = _clock.Now;
            ev.LastClipIndex = clipIndex;
            _totalStarted++;
            return AudioPlayResult.Started;
        }

        private bool TryResolveChannel(AudioEventRuntime ev, out ChannelState channel)
        {
            if (ev.ChannelIndex == -2)
                ev.ChannelIndex = _channelIndex.GetValueOrDefault(ev.Definition.Channel, -1);

            channel = ev.ChannelIndex >= 0 ? _channels[ev.ChannelIndex] : null;
            return channel != null;
        }

        private ChannelState RequireChannel(AudioChannelId id)
        {
            ThrowIfDisposed();
            return _channelIndex.TryGetValue(id, out var index)
                ? _channels[index]
                : throw new InvalidOperationException($"Audio channel '{id.Value}' is not registered.");
        }

        private bool IsLive(int slot, int generation)
            => (uint)slot < (uint)_playbacks.Length && _playbacks[slot].Active && _playbacks[slot].Generation == generation;

        private void StopPlaybackAt(int slot, float fadeSeconds)
        {
            if (fadeSeconds <= 0f)
            {
                ReleasePlayback(slot);
                return;
            }

            ref var playback = ref _playbacks[slot];
            playback.FadeFrom = playback.Gain;
            playback.FadeTo = 0f;
            playback.FadeStart = _clock.Now;
            playback.FadeDuration = fadeSeconds;
            playback.StopAtFadeEnd = true;
        }

        private void ReleasePlayback(int slot)
        {
            ref var playback = ref _playbacks[slot];
            if (!playback.Active)
                return;

            _backend.StopPlayback(slot);
            playback.Lease?.Dispose();
            playback.Channel.ActivePlaybacks--;
            if (playback.Event != null)
                playback.Event.ActivePlaybacks--;

            _activePlaybacks--;
            playback.Active = false;
            playback.Event = null;
            playback.Channel = null;
            playback.Lease = null;
            playback.Generation++;
        }

        private int FindOldestPlaybackOf(AudioEventRuntime ev)
        {
            var oldest = -1;
            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                if (_playbacks[slot].Active && _playbacks[slot].Event == ev
                    && (oldest < 0 || _playbacks[slot].StartTime < _playbacks[oldest].StartTime))
                    oldest = slot;
            }

            return oldest;
        }
        
        private int FindStealCandidate(ChannelState channel, int newPriority, int excludeA, int excludeB)
        {
            var best = -1;
            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                ref var playback = ref _playbacks[slot];
                if (!playback.Active || slot == excludeA || slot == excludeB || playback.Priority > newPriority)
                    continue;

                if (channel != null && playback.Channel != channel)
                    continue;

                if (best < 0 || playback.Priority < _playbacks[best].Priority
                    || (playback.Priority == _playbacks[best].Priority && playback.StartTime < _playbacks[best].StartTime))
                    best = slot;
            }

            return best;
        }

        private float RandomRange(Vector2 range)
            => range.y > range.x ? _random.NextFloat(range.x, range.y) : range.x;

        private float ComputePlaybackVolume(in Playback playback)
            => Mathf.Clamp01(playback.EventVolume * playback.HandleVolume * playback.Gain * playback.Channel.AppliedGainOrBase);

        private void ApplyPlaybackVolume(int slot) => _backend.SetPlaybackVolume(slot, ComputePlaybackVolume(in _playbacks[slot]));
        
        private void OnSettingsChanged() => RecomputeBaseGains();

        private void RecomputeBaseGains()
        {
            foreach (var channel in _channels)
            {
                var definition = channel.Definition;
                channel.BaseGain = _settings.IsChannelMuted(definition.Id)
                    ? 0f
                    : definition.Volume * _settings.GetChannelVolume(definition.Id) 
                                        * _settings.GetOutputGroupVolume(definition.OutputGroupId);
                if (channel.AppliedGain < 0f)
                    channel.AppliedGain = channel.BaseGain * channel.DuckGain * channel.UserFade;
            }

            _backend.SetMasterVolume(_settings.MasterVolume);
        }

        private void ConfigureDucking(IReadOnlyList<AudioDuckingRule> rules)
        {
            if (rules == null)
                return;

            var triggers = new List<(ChannelState trigger, ChannelState target, float gain)>(rules.Count);
            foreach (var rule in rules)
            {
                if (!_channelIndex.TryGetValue(rule.Trigger, out var triggerIndex) 
                    || !_channelIndex.TryGetValue(rule.Target, out var targetIndex))
                    throw new InvalidOperationException($"Ducking rule {rule.Trigger.Value} -> {rule.Target.Value} refers to a channel that is not registered.");

                var target = _channels[targetIndex];
                var depth = 1f - rule.TargetGain;
                if (depth > target.DuckDepth)
                {
                    target.DuckDepth = depth;
                    target.DuckAttack = rule.AttackSeconds;
                    target.DuckRelease = rule.ReleaseSeconds;
                }

                triggers.Add((_channels[triggerIndex], target, rule.TargetGain));
            }

            _duckRules = triggers.ToArray();
        }

        private void UpdateDucking(float deltaTime)
        {
            if (_duckRules.Length == 0)
                return;

            foreach (var channel in _channels)
                channel.DuckTarget = 1f;

            foreach (var rule in _duckRules)
            {
                if (rule.trigger.ActivePlaybacks > 0 || rule.trigger.CurrentSlot >= 0)
                    rule.target.DuckTarget = Math.Min(rule.target.DuckTarget, rule.gain);
            }

            foreach (var channel in _channels)
            {
                if (channel.DuckDepth <= 0f)
                    continue;

                if (channel.DuckGain > channel.DuckTarget)
                {
                    var step = channel.DuckAttack > 0f ? (channel.DuckDepth / channel.DuckAttack) * deltaTime : float.MaxValue;
                    channel.DuckGain = Math.Max(channel.DuckTarget, channel.DuckGain - step);
                }
                else if (channel.DuckGain < channel.DuckTarget)
                {
                    var step = channel.DuckRelease > 0f ? (channel.DuckDepth / channel.DuckRelease) * deltaTime : float.MaxValue;
                    channel.DuckGain = Math.Min(channel.DuckTarget, channel.DuckGain + step);
                }
            }
        }

        private void UpdateChannelFade(ChannelState channel, double now)
        {
            if (channel.UserFadeDuration <= 0f)
                return;

            var progress = (float)((now - channel.UserFadeStart) / channel.UserFadeDuration);
            if (progress >= 1f)
            {
                channel.UserFade = channel.UserFadeTo;
                channel.UserFadeDuration = 0f;
                return;
            }

            channel.UserFade = Mathf.Lerp(channel.UserFadeFrom, channel.UserFadeTo, progress);
        }

        private void TickPlaybacks(double now)
        {
            for (var slot = 0; slot < _playbacks.Length; slot++)
            {
                ref var playback = ref _playbacks[slot];
                if (!playback.Active)
                    continue;

                var dirty = _channelChanged[playback.Channel.Index];
                if (playback.FadeDuration > 0f)
                {
                    var progress = (float)((now - playback.FadeStart) / playback.FadeDuration);
                    if (progress >= 1f)
                    {
                        playback.Gain = playback.FadeTo;
                        playback.FadeDuration = 0f;
                        if (playback.StopAtFadeEnd)
                        {
                            ReleasePlayback(slot);
                            continue;
                        }
                    }
                    else
                    {
                        playback.Gain = Mathf.Lerp(playback.FadeFrom, playback.FadeTo, progress);
                    }

                    dirty = true;
                }

                if (!playback.Loop && now >= playback.ExpectedEnd && !_backend.IsSlotPlaying(slot))
                {
                    ReleasePlayback(slot);
                    continue;
                }

                if (dirty)
                    ApplyPlaybackVolume(slot);
            }
        }

        private void TickTracks(double now)
        {
            if (_activeTracks == 0)
                return;

            for (var index = 0; index < _channels.Length; index++)
            {
                var channel = _channels[index];
                for (var slot = 0; slot < channel.Slots.Length; slot++)
                {
                    ref var track = ref channel.Slots[slot];
                    if (!track.Active)
                        continue;

                    var dirty = _channelChanged[index];
                    if (track.FadeDuration > 0f)
                    {
                        var progress = (float)((now - track.FadeStart) / track.FadeDuration);
                        if (progress >= 1f)
                        {
                            track.Gain = track.FadeTo;
                            track.FadeDuration = 0f;
                            if (track.StopAtFadeEnd)
                            {
                                ReleaseTrackSlot(channel, slot);
                                continue;
                            }
                        }
                        else
                        {
                            track.Gain = Mathf.Lerp(track.FadeFrom, track.FadeTo, progress);
                        }

                        dirty = true;
                    }

                    if (!track.Loop && now >= track.ExpectedEnd && !_backend.IsTrackPlaying(index, slot))
                    {
                        ReleaseTrackSlot(channel, slot);
                        continue;
                    }

                    if (dirty)
                        _backend.SetTrackVolume(index, slot, ComputeTrackVolume(channel, in track));
                }
            }
        }

        private float ComputeTrackVolume(ChannelState channel, in TrackSlot track)
            => Mathf.Clamp01(track.BaseVolume * track.Gain * channel.AppliedGainOrBase);

        private void BeginTrack(ChannelState channel, int slot, AudioClip clip, string key, IDisposable lease, bool loop, float volume, float pitch, float fadeInSeconds)
        {
            pitch = Mathf.Max(MinPitch, pitch);
            ref var track = ref channel.Slots[slot];
            track = new TrackSlot
            {
                Active = true,
                Key = key,
                Lease = lease,
                BaseVolume = Mathf.Clamp01(volume),
                Gain = fadeInSeconds > 0f ? 0f : 1f,
                Loop = loop,
                ExpectedEnd = _clock.Now + (clip.length / Math.Abs(pitch)) + EndMargin,
            };
            if (fadeInSeconds > 0f)
                StartTrackFade(ref track, 1f, fadeInSeconds, stopAtEnd: false);

            channel.CurrentSlot = slot;
            _activeTracks++;
            _backend.StartTrack(channel.Index, slot, clip, ComputeTrackVolume(channel, in track), pitch, loop);
        }

        private void StartTrackFade(ref TrackSlot track, float target, float seconds, bool stopAtEnd)
        {
            track.FadeFrom = track.Gain;
            track.FadeTo = target;
            track.FadeStart = _clock.Now;
            track.FadeDuration = seconds;
            track.StopAtFadeEnd = stopAtEnd;
        }

        private void StopTrackInternal(ChannelState channel, float fadeSeconds)
        {
            for (var slot = 0; slot < channel.Slots.Length; slot++)
            {
                if (!channel.Slots[slot].Active)
                    continue;

                if (fadeSeconds <= 0f)
                    ReleaseTrackSlot(channel, slot);
                else
                    StartTrackFade(ref channel.Slots[slot], 0f, fadeSeconds, stopAtEnd: true);
            }

            if (fadeSeconds <= 0f)
                channel.CurrentSlot = -1;
        }

        private void ReleaseAllTrackSlots(ChannelState channel)
        {
            for (var slot = 0; slot < channel.Slots.Length; slot++)
                ReleaseTrackSlot(channel, slot);
        }

        private void ReleaseTrackSlot(ChannelState channel, int slot)
        {
            ref var track = ref channel.Slots[slot];
            if (!track.Active)
                return;

            _backend.StopTrack(channel.Index, slot);
            track.Lease?.Dispose();
            track = default;
            _activeTracks--;
            if (channel.CurrentSlot == slot)
                channel.CurrentSlot = -1;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioEngine));
        }

        private struct Playback
        {
            public bool Active;
            public int Generation;
            public AudioEventRuntime Event;
            public ChannelState Channel;
            public int Priority;
            public double StartTime;
            public double ExpectedEnd;
            public float EventVolume;
            public float HandleVolume;
            public float Pitch;
            public bool Loop;
            public IDisposable Lease;
            public float Gain;
            public float FadeFrom;
            public float FadeTo;
            public double FadeStart;
            public float FadeDuration;
            public bool StopAtFadeEnd;
        }

        private struct TrackSlot
        {
            public bool Active;
            public string Key;
            public IDisposable Lease;
            public float BaseVolume;
            public float Gain;
            public float FadeFrom;
            public float FadeTo;
            public double FadeStart;
            public float FadeDuration;
            public bool StopAtFadeEnd;
            public bool Loop;
            public double ExpectedEnd;
        }

        private sealed class ChannelState
        {
            public int ActivePlaybacks;
            public float BaseGain;
            public float AppliedGain;
            public float DuckGain = 1f;
            public float DuckTarget = 1f;
            public float DuckDepth;
            public float DuckAttack;
            public float DuckRelease;
            public float UserFade = 1f;
            public float UserFadeFrom;
            public float UserFadeTo;
            public double UserFadeStart;
            public float UserFadeDuration;
            public int CurrentSlot = -1;

            public readonly TrackSlot[] Slots = new TrackSlot[2];

            public IAudioChannelDefinition Definition { get; }
            public AudioChannelId Id { get; }
            public int Index { get; }
            public int MaxPlaybacks { get; }

            public float AppliedGainOrBase => AppliedGain >= 0f ? AppliedGain : BaseGain;

            public ChannelState(IAudioChannelDefinition definition, int index)
            {
                Definition = definition;
                Id = new(definition.Id);
                Index = index;
                MaxPlaybacks = definition.MaxOneShotCount;
                AppliedGain = -1f;
            }
        }
    }
    
    public static class AudioDiagnostics
    {
        public static AudioEngine Current { get; internal set; }
    }
}
