using System;
using System.Collections.Generic;

namespace Foundation.Audio
{
    internal sealed class AudioEventRuntime
    {
        public AudioEventDefinition Definition { get; }

        /// <summary> -2 not resolved yet, -1 channel unknown, otherwise the engine's channel index.</summary>
        public int ChannelIndex { get; set; }

        public double LastPlayTime { get; set; }

        public int LastClipIndex { get; set; }

        public int ActivePlaybacks { get; set; }

        public AudioEventRuntime(AudioEventDefinition definition)
        {
            Definition = definition;
            ChannelIndex = -2;
            LastPlayTime = double.NegativeInfinity;
            LastClipIndex = -1;
        }
    }

    /// <summary>
    /// The events the game can play, indexed by id. Libraries are registered when their content loads and
    /// removed when it unloads. A duplicate id is a content error and is rejected loudly.
    /// </summary>
    public sealed class AudioEventRegistry
    {
        private readonly Dictionary<AudioEventId, AudioEventRuntime> _events = new();
        private readonly Dictionary<AudioEventLibrary, List<AudioEventId>> _libraries = new();

        public int Count => _events.Count;

        public bool Contains(AudioEventId id) => _events.ContainsKey(id);

        public bool TryGetDefinition(AudioEventId id, out AudioEventDefinition definition)
        {
            if (_events.TryGetValue(id, out var runtime))
            {
                definition = runtime.Definition;
                return true;
            }

            definition = null;
            return false;
        }

        public void Register(AudioEventDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (definition.Id.IsEmpty)
                throw new ArgumentException($"Audio event '{definition.DisplayName}' has no id.", nameof(definition));

            if (_events.ContainsKey(definition.Id))
                throw new InvalidOperationException($"Audio event '{definition.Id.Value}' is registered more than once.");

            _events.Add(definition.Id, new AudioEventRuntime(definition));
        }
        
        public void Register(AudioEventLibrary library)
        {
            if (library == null)
                throw new ArgumentNullException(nameof(library));

            if (_libraries.ContainsKey(library))
                return;

            var added = new List<AudioEventId>(library.Events.Count);
            try
            {
                foreach (var audioEventDef in library.Events)
                {
                    Register(audioEventDef);
                    added.Add(audioEventDef.Id);
                }
            }
            catch
            {
                foreach (var audioEventId in added)
                    _events.Remove(audioEventId);

                throw;
            }

            _libraries.Add(library, added);
        }

        public void Unregister(AudioEventLibrary library)
        {
            if (library == null || !_libraries.TryGetValue(library, out var ids))
                return;

            foreach (var audioEventId in ids)
                _events.Remove(audioEventId);

            _libraries.Remove(library);
        }

        public void CollectDefinitions(List<AudioEventDefinition> results)
        {
            foreach (var runtime in _events.Values)
                results.Add(runtime.Definition);
        }

        internal bool TryGetRuntime(AudioEventId id, out AudioEventRuntime runtime) => _events.TryGetValue(id, out runtime);
    }
}
