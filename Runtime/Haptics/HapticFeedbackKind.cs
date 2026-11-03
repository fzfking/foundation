namespace Foundation.Haptics
{
    /// <summary>
    /// Game-agnostic feedback intents. Each <see cref="IHapticsService"/> backend maps them to its plugin, so
    /// callers never reference a third-party haptics type.
    /// </summary>
    public enum HapticFeedbackKind
    {
        LightTap,
        MediumTap,
        HeavyTap,
        Success,
        Warning,
        Failure,
    }
}
