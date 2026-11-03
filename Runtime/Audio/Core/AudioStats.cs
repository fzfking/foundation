namespace Foundation.Audio
{
    /// <summary>A copy of the engine's counters for diagnostics. Taking one allocates nothing.</summary>
    public readonly struct AudioStats
    {
        public int ActivePlaybacks { get; }
        public int ActiveTracks { get; }
        public int MaxPlaybacks { get; }
        public int RequestsLastSecond { get; }
        public long TotalRequests { get; }
        public long TotalStarted { get; }
        public long PlaybackRejects { get; }
        public long CooldownRejects { get; }
        public long ClipNotLoadedRejects { get; }
        public long Steals { get; }

        public AudioStats(
            int activePlaybacks, int activeTracks, int maxPlaybacks, int requestsLastSecond,
            long totalRequests, long totalStarted, long playbackRejects, long cooldownRejects,
            long clipNotLoadedRejects, long steals)
        {
            ActivePlaybacks = activePlaybacks;
            ActiveTracks = activeTracks;
            MaxPlaybacks = maxPlaybacks;
            RequestsLastSecond = requestsLastSecond;
            TotalRequests = totalRequests;
            TotalStarted = totalStarted;
            PlaybackRejects = playbackRejects;
            CooldownRejects = cooldownRejects;
            ClipNotLoadedRejects = clipNotLoadedRejects;
            Steals = steals;
        }
    }

    public readonly struct AudioChannelSnapshot
    {
        public AudioChannelId Id { get; }

        /// <summary>Master x channel x group x duck x fade, as applied to the channel's sounds.</summary>
        public float EffectiveVolume { get; }

        public bool Muted { get; }
        public int ActivePlaybacks { get; }
        public int MaxPlaybacks { get; }
        public float DuckGain { get; }
        public bool HasTrack { get; }

        public AudioChannelSnapshot(AudioChannelId id, float effectiveVolume, bool muted, int activePlaybacks, int maxPlaybacks, float duckGain, bool hasTrack)
        {
            Id = id;
            EffectiveVolume = effectiveVolume;
            Muted = muted;
            ActivePlaybacks = activePlaybacks;
            MaxPlaybacks = maxPlaybacks;
            DuckGain = duckGain;
            HasTrack = hasTrack;
        }
    }
}
