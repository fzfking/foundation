using System;
using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// One clip an event can play: a direct reference (resident with the library) or an asset id the
    /// project's asset provider loads (kept out of memory until preloaded).
    /// </summary>
    [Serializable]
    public struct AudioClipEntry
    {
        [SerializeField] private AudioClip clip;
        [SerializeField] private string assetId;
        [SerializeField, Min(0f)] private float weight;

        public AudioClip Clip => clip;

        /// <summary>The asset provider key, used when <see cref="Clip"/> is not set.</summary>
        public string AssetId => assetId;

        /// <summary>Relative chance for <see cref="AudioSelectionMode.WeightedRandom"/>. A default entry weighs 1.</summary>
        public float Weight => weight <= 0f ? (clip == null && string.IsNullOrEmpty(assetId) ? 0f : 1f) : weight;

        public bool IsEmpty => clip == null && string.IsNullOrEmpty(assetId);

        public AudioClipEntry(AudioClip clip, float weight = 1f)
        {
            this.clip = clip;
            assetId = null;
            this.weight = weight;
        }

        public AudioClipEntry(string assetId, float weight = 1f)
        {
            clip = null;
            this.assetId = assetId;
            this.weight = weight;
        }
    }
}
