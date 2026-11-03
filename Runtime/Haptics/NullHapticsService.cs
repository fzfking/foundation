namespace Foundation.Haptics
{
    public sealed class NullHapticsService : IHapticsService
    {
        public bool IsSupported => false;

        public void Play(HapticFeedbackKind kind)
        {
        }
    }
}
