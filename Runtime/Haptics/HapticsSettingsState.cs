using System;

namespace Foundation.Haptics
{
    public sealed class HapticsSettingsState : IHapticsSettings
    {
        private bool _enabled = true;

        public bool Enabled => _enabled;

        public event Action Changed;

        public void SetEnabled(bool enabled)
        {
            if (_enabled == enabled)
                return;

            _enabled = enabled;
            Changed?.Invoke();
        }
    }
}
