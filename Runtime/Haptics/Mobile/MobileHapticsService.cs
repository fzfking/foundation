using tsyk5.MobileHapticFeedback;

namespace Foundation.Haptics.Mobile
{
    /// <summary>
    /// Backs <see cref="IHapticsService"/> with the tsyk5/MobileHapticFeedback plugin; respects
    /// <see cref="IHapticsSettings"/> itself.
    /// </summary>
    public sealed class MobileHapticsService : IHapticsService
    {
        private readonly IHapticsSettings _settings;

        public bool IsSupported => MobileHapticFeedback.IsSupported;

        public MobileHapticsService(IHapticsSettings settings)
        {
            _settings = settings;
        }

        public void Play(HapticFeedbackKind kind)
        {
            if (_settings is { Enabled: false })
                return;

            switch (kind)
            {
                case HapticFeedbackKind.LightTap:
                    MobileHapticFeedback.PlayImpact(ImpactStyle.Light);
                    break;
                case HapticFeedbackKind.MediumTap:
                    MobileHapticFeedback.PlayImpact(ImpactStyle.Medium);
                    break;
                case HapticFeedbackKind.HeavyTap:
                    MobileHapticFeedback.PlayImpact(ImpactStyle.Heavy);
                    break;
                case HapticFeedbackKind.Success:
                    MobileHapticFeedback.PlayNotification(NotificationType.Success);
                    break;
                case HapticFeedbackKind.Warning:
                    MobileHapticFeedback.PlayNotification(NotificationType.Warning);
                    break;
                case HapticFeedbackKind.Failure:
                    MobileHapticFeedback.PlayNotification(NotificationType.Error);
                    break;
            }
        }
    }
}
