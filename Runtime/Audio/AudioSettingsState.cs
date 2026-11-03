using System;
using System.Collections.Generic;

namespace Foundation.Audio
{
    public sealed class AudioSettingsState : IAudioSettings
    {
        private readonly Dictionary<string, float> _channelVolumes;
        private readonly Dictionary<string, float> _outputGroupVolumes;
        private readonly HashSet<string> _mutedChannels = new(StringComparer.Ordinal);
        private float _masterVolume = 1f;

        public float MasterVolume => _masterVolume;

        public event Action Changed;

        public AudioSettingsState(IReadOnlyList<IAudioChannelDefinition> definitions)
        {
            _channelVolumes = new(StringComparer.Ordinal);
            _outputGroupVolumes = new(StringComparer.Ordinal);
            foreach (var definition in definitions ?? Array.Empty<IAudioChannelDefinition>())
            {
                _channelVolumes[definition.Id] = 1f;
                if (!string.IsNullOrWhiteSpace(definition.OutputGroupId))
                    _outputGroupVolumes[definition.OutputGroupId] = 1f;
            }
        }

        public float GetChannelVolume(string channelId)
            => channelId != null && _channelVolumes.TryGetValue(channelId, out var volume) ? volume : 1f;

        public float GetOutputGroupVolume(string outputGroupId)
            => outputGroupId != null && _outputGroupVolumes.TryGetValue(outputGroupId, out var volume) ? volume : 1f;

        public bool IsChannelMuted(string channelId) => channelId != null && _mutedChannels.Contains(channelId);

        public void SetChannelMuted(string channelId, bool muted)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                throw new ArgumentException("Audio channel id is required.", nameof(channelId));

            var changed = muted ? _mutedChannels.Add(channelId) : _mutedChannels.Remove(channelId);
            if (changed)
                Changed?.Invoke();
        }

        public void SetMasterVolume(float volume)
        {
            var clamped = Clamp(volume);
            if (Math.Abs(_masterVolume - clamped) < 0.0001f)
                return;

            _masterVolume = clamped;
            Changed?.Invoke();
        }

        public void SetChannelVolume(string channelId, float volume)
        {
            if (string.IsNullOrWhiteSpace(channelId))
                throw new ArgumentException("Audio channel id is required.", nameof(channelId));

            var clamped = Clamp(volume);
            if (_channelVolumes.TryGetValue(channelId, out var current) && Math.Abs(current - clamped) < 0.0001f)
                return;

            _channelVolumes[channelId] = clamped;
            Changed?.Invoke();
        }

        public void SetOutputGroupVolume(string outputGroupId, float volume)
        {
            if (string.IsNullOrWhiteSpace(outputGroupId))
                throw new ArgumentException("Audio output group id is required.", nameof(outputGroupId));

            var clamped = Clamp(volume);
            if (_outputGroupVolumes.TryGetValue(outputGroupId, out var current) && Math.Abs(current - clamped) < 0.0001f)
                return;

            _outputGroupVolumes[outputGroupId] = clamped;
            Changed?.Invoke();
        }

        private static float Clamp(float volume) => Math.Clamp(volume, 0f, 1f);
    }
}
