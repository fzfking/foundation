using System;

namespace Foundation.Haptics
{
    public interface IHapticsSettings
    {
        bool Enabled { get; }

        event Action Changed;

        void SetEnabled(bool enabled);
    }
}
