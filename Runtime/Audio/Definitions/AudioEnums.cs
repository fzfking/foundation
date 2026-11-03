namespace Foundation.Audio
{
    /// <summary>How an event with several clips picks the next one.</summary>
    public enum AudioSelectionMode
    {
        Single,
        Random,
        /// <summary>Uniformly random, never the clip that played last. Best for repeated UI and impact sounds.</summary>
        RandomNoImmediateRepeat,
        WeightedRandom,
    }

    /// <summary>What happens when an event already has its maximum number of simultaneous playbacks.</summary>
    public enum AudioPlaybackOverflowPolicy
    {
        IgnoreNew,
        StopOldest,
    }

    /// <summary>The outcome of a play request. Only <see cref="Started"/> produces sound.</summary>
    public enum AudioPlayResult
    {
        Started,
        UnknownEvent,
        UnknownChannel,
        NoClips,
        
        /// <summary>The clip is addressable and not resident yet. A load was started; preload events that must never miss.</summary>
        ClipNotLoaded,
        Cooldown,
        EventPlaybackLimit,
        ChannelFull,
        Saturated,

        /// <summary>The channel is muted or at zero volume, so no playback was spent.</summary>
        Silenced,
        BackendRejected,
        Disposed,
    }
}
