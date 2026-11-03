using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Audio
{
    /// <summary>
    /// An authored set of audio events (global UI, one per level or feature), registered in the
    /// <see cref="AudioEventRegistry"/> when its content loads.
    /// </summary>
    [CreateAssetMenu(menuName = "Foundation/Audio/Event Library", fileName = "AudioEventLibrary")]
    public sealed class AudioEventLibrary : ScriptableObject
    {
        [SerializeField] private List<AudioEventDefinition> events = new();

        public IReadOnlyList<AudioEventDefinition> Events => events;

        public static AudioEventLibrary Create(IEnumerable<AudioEventDefinition> definitions)
        {
            var library = CreateInstance<AudioEventLibrary>();
            library.events.AddRange(definitions);
            return library;
        }
    }
}
