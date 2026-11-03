using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// Optional per-play adjustments. A scale left at 0 means "unchanged" (1), so <c>default</c> is a neutral
    /// set of options.
    /// </summary>
    public readonly struct AudioPlayOptions
    {
        private readonly float _volumeScale;
        private readonly float _pitchScale;

        public float VolumeScale => _volumeScale > 0f ? _volumeScale : 1f;

        public float PitchScale => _pitchScale > 0f ? _pitchScale : 1f;

        public Vector3 Position { get; }
        public bool HasPosition { get; }
        public Transform Follow { get; }

        private AudioPlayOptions(float volumeScale, float pitchScale, Vector3 position, bool hasPosition, Transform follow)
        {
            _volumeScale = volumeScale;
            _pitchScale = pitchScale;
            Position = position;
            HasPosition = hasPosition;
            Follow = follow;
        }

        public static AudioPlayOptions At(Vector3 position) => new(0f, 0f, position, true, null);

        public static AudioPlayOptions Following(Transform target) => new(0f, 0f, target != null ? target.position : Vector3.zero, true, target);

        public AudioPlayOptions WithVolume(float scale) => new(scale, _pitchScale, Position, HasPosition, Follow);

        public AudioPlayOptions WithPitch(float scale) => new(_volumeScale, scale, Position, HasPosition, Follow);
    }
}
