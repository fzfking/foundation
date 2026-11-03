using System;
using System.Collections.Generic;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Keeps pre-load descriptors apart from concrete definitions. Definitions are published after their
    /// module handle is loaded and removed after teardown.
    /// </summary>
    public sealed class MaterializedGameContextCatalog : IGameContextCatalog
    {
        private readonly Dictionary<GameContextId, GameContextDescriptor> _descriptors = new();
        private readonly Dictionary<string, GameContextId> _descriptorBySaveSlot = new(StringComparer.Ordinal);
        private readonly Dictionary<GameContextId, GameContextDefinition> _definitions = new();
        private readonly List<GameContextDescriptor> _descriptorList = new();
        private readonly List<GameContextDefinition> _definitionList = new();

        public GameContextId FallbackContextId { get; }

        public IReadOnlyList<GameContextDescriptor> Descriptors => _descriptorList;

        public IReadOnlyList<GameContextDefinition> Definitions => _definitionList;

        public MaterializedGameContextCatalog(
            IEnumerable<GameContextDescriptor> descriptors,
            GameContextId fallbackContextId)
        {
            foreach (var descriptor in descriptors ?? throw new ArgumentNullException(nameof(descriptors)))
            {
                if (descriptor == null)
                    throw new ArgumentException("Context descriptor catalog contains a null descriptor.", nameof(descriptors));
                if (!_descriptors.TryAdd(descriptor.Id, descriptor))
                    throw new InvalidOperationException($"Context '{descriptor.Id}' is described more than once.");

                if (!_descriptorBySaveSlot.TryAdd(descriptor.Save.SlotId, descriptor.Id))
                {
                    throw new InvalidOperationException(
                        $"Contexts '{_descriptorBySaveSlot[descriptor.Save.SlotId]}' and '{descriptor.Id}' use save slot '{descriptor.Save.SlotId}'.");
                }

                _descriptorList.Add(descriptor);
            }

            if (!_descriptors.ContainsKey(fallbackContextId))
                throw new ArgumentException($"Fallback context '{fallbackContextId}' is not described.", nameof(fallbackContextId));

            FallbackContextId = fallbackContextId;
        }

        public bool Contains(GameContextId id) => _descriptors.ContainsKey(id);

        /// <summary>
        /// Adds a context described after the catalog was built (by the module manifest config). An already
        /// described context, compiled-in ones included, is never replaced, so a config cannot redefine an
        /// existing save profile.
        /// </summary>
        public bool TryAddDescriptor(GameContextDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));
            if (_descriptors.ContainsKey(descriptor.Id) || _descriptorBySaveSlot.ContainsKey(descriptor.Save.SlotId))
                return false;

            _descriptors.Add(descriptor.Id, descriptor);
            _descriptorBySaveSlot.Add(descriptor.Save.SlotId, descriptor.Id);
            _descriptorList.Add(descriptor);
            return true;
        }

        public bool TryGetDescriptor(GameContextId id, out GameContextDescriptor descriptor)
            => _descriptors.TryGetValue(id, out descriptor);

        public GameContextDescriptor GetDescriptor(GameContextId id)
            => _descriptors.TryGetValue(id, out var descriptor)
                ? descriptor
                : throw new InvalidOperationException($"No context descriptor registered with id '{id}'.");

        public bool TryGet(GameContextId id, out GameContextDefinition definition)
            => _definitions.TryGetValue(id, out definition);

        public GameContextDefinition Get(GameContextId id)
            => _definitions.TryGetValue(id, out var definition)
                ? definition
                : throw new InvalidOperationException($"Context '{id}' has not been materialized from its module yet.");

        public void Materialize(GameContextDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            
            if (!_descriptors.TryGetValue(definition.Id, out var descriptor))
                throw new InvalidOperationException($"Context '{definition.Id}' has no AOT descriptor.");
            if (descriptor.Save.SlotId != definition.Save.SlotId)
            {
                throw new InvalidOperationException(
                    $"Context '{definition.Id}' cannot be materialized with a save slot different from its descriptor.");
            }

            if (_definitions.TryGetValue(definition.Id, out var previous))
                _definitionList.Remove(previous);

            _definitions[definition.Id] = definition;
            _definitionList.Add(definition);
        }

        public void Dematerialize(GameContextId id)
        {
            if (!_definitions.Remove(id, out var definition))
                return;

            _definitionList.Remove(definition);
        }
    }
}
