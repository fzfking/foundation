namespace Foundation.Diagnostics
{
    /// <summary>
    /// What the frames of one stretch of play looked like: how many, how long they took on the whole, how many were slow and
    /// how many were stalls. Plain arithmetic on counters, so counting costs the player nothing; no allocation, no history.
    /// </summary>
    public sealed class FrameHealthAccumulator
    {
        /// <summary>A frame slower than this misses 30 fps.</summary>
        public const float SlowFrameSeconds = 1f / 30f;

        /// <summary>A frame this long is a visible stall.</summary>
        public const float HitchSeconds = 0.25f;

        /// <summary>A frame longer than this was not play (a loading screen, a resume, a debugger); it is not counted at all.</summary>
        public const float IgnoredFrameSeconds = 2f;

        public int Frames { get; private set; }
        public double Seconds { get; private set; }
        public int SlowFrames { get; private set; }
        public int Hitches { get; private set; }

        public double AverageFps => Seconds > 0d ? Frames / Seconds : 0d;
        public double SlowFramePercent => Frames > 0 ? 100d * SlowFrames / Frames : 0d;

        public void Add(float frameSeconds)
        {
            if (frameSeconds is <= 0f or >= IgnoredFrameSeconds)
                return;

            Frames++;
            Seconds += frameSeconds;
            if (frameSeconds > SlowFrameSeconds)
                SlowFrames++;
            if (frameSeconds > HitchSeconds)
                Hitches++;
        }

        public void Reset()
        {
            Frames = 0;
            Seconds = 0d;
            SlowFrames = 0;
            Hitches = 0;
        }
    }
}
