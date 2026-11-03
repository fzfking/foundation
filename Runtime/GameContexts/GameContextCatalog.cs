using System;
using System.Collections.Generic;

namespace Foundation.GameContexts
{
    public interface IGameContextCatalog
    {
        /// <summary>
        /// The context entered when no other choice is valid: an unknown or no longer available persisted
        /// selection, or nothing persisted. Always the main game.
        /// </summary>
        GameContextId FallbackContextId { get; }
        IReadOnlyList<GameContextDefinition> Definitions { get; }
        bool Contains(GameContextId id);
        bool TryGet(GameContextId id, out GameContextDefinition definition);
        GameContextDefinition Get(GameContextId id);
    }

    /// <summary>The contexts this build can run and the fallback one. Authored once in the composition root.</summary>
    public sealed class GameContextCatalog : IGameContextCatalog
    {
        private readonly Dictionary<GameContextId, GameContextDefinition> _byId = new();
        private readonly Dictionary<string, GameContextId> _bySaveSlot = new(StringComparer.Ordinal);
        private readonly List<GameContextDefinition> _definitions = new();

        public GameContextId FallbackContextId { get; }

        public IReadOnlyList<GameContextDefinition> Definitions => _definitions;

        public GameContextCatalog(
            IEnumerable<GameContextDefinition> definitions,
            GameContextId fallbackContextId)
        {
            foreach (var definition in definitions ?? throw new ArgumentNullException(nameof(definitions)))
            {
                if (definition == null)
                    throw new ArgumentException("Game context catalog contains a null definition.", nameof(definitions));

                if (!_byId.TryAdd(definition.Id, definition))
                    throw new InvalidOperationException($"Game context '{definition.Id}' is registered twice.");

                if (!_bySaveSlot.TryAdd(definition.Save.SlotId, definition.Id))
                {
                    throw new InvalidOperationException(
                        $"Game contexts '{_bySaveSlot[definition.Save.SlotId]}' and '{definition.Id}' use save slot '{definition.Save.SlotId}'.");
                }

                _definitions.Add(definition);
            }

            if (!_byId.ContainsKey(fallbackContextId))
            {
                throw new ArgumentException(
                    $"Fallback game context '{fallbackContextId}' is not part of the catalog.",
                    nameof(fallbackContextId));
            }

            FallbackContextId = fallbackContextId;
        }

        public bool Contains(GameContextId id) => _byId.ContainsKey(id);

        public bool TryGet(GameContextId id, out GameContextDefinition definition)
            => _byId.TryGetValue(id, out definition);

        public GameContextDefinition Get(GameContextId id)
            => _byId.TryGetValue(id, out var definition)
                ? definition
                : throw new InvalidOperationException($"No game context registered with id '{id}'.");
    }
}
