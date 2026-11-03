namespace Foundation.Audio
{
    /// <summary>
    /// A cheap reference to one running sound. Stops being valid when the playback ends; a stale handle is
    /// always safe and does nothing, because a reused slot has a different generation.
    /// </summary>
    public readonly struct AudioHandle
    {
        private readonly AudioEngine _engine;
        private readonly int _slot;
        private readonly int _generation;

        public static AudioHandle Invalid => default;

        public bool IsPlaying => _engine != null 
                                 && _engine.IsPlaying(_slot, _generation);

        internal AudioHandle(AudioEngine engine, int slot, int generation)
        {
            _engine = engine;
            _slot = slot;
            _generation = generation;
        }

        public void Stop()
        {
            _engine?.StopPlayback(_slot, _generation, 0f);
        }

        public void FadeOut(float seconds)
        {
            _engine?.StopPlayback(_slot, _generation, seconds);
        }

        public void SetVolume(float volume)
        {
            _engine?.SetPlaybackVolume(_slot, _generation, volume);
        }

        public void SetPitch(float pitch)
        {
            _engine?.SetPlaybackPitch(_slot, _generation, pitch);
        }
    }
}
