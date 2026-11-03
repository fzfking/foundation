namespace Foundation.Haptics
{
    /// <summary>
    /// Plays device haptic feedback. Implementations respect <see cref="IHapticsSettings"/> themselves (no-op
    /// while disabled), so callers need not check.
    /// </summary>
    public interface IHapticsService
    {
        bool IsSupported { get; }

        void Play(HapticFeedbackKind kind);
    }
}
