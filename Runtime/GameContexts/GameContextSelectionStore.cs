using UnityEngine;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Remembers which context was last active, so a restart resumes the same game. Deliberately not a save
    /// segment: it must be readable before any context namespace exists and survive a context's save being
    /// wiped.
    /// </summary>
    public interface IGameContextSelectionStore
    {
        bool TryLoadLastActive(out GameContextId contextId);

        void SaveLastActive(GameContextId contextId);

        void Clear();
    }

    public sealed class PlayerPrefsGameContextSelectionStore : IGameContextSelectionStore
    {
        public const string DefaultKey = "foundation.game-context.last-active";

        private readonly string _key;

        public PlayerPrefsGameContextSelectionStore(string key = DefaultKey)
        {
            _key = string.IsNullOrWhiteSpace(key) ? DefaultKey : key;
        }

        public bool TryLoadLastActive(out GameContextId contextId)
        {
            var stored = PlayerPrefs.GetString(_key, string.Empty);
            if (string.IsNullOrWhiteSpace(stored))
            {
                contextId = default;
                return false;
            }

            contextId = new GameContextId(stored);
            return true;
        }

        public void SaveLastActive(GameContextId contextId)
        {
            if (contextId.IsEmpty)
                return;

            PlayerPrefs.SetString(_key, contextId.Value);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(_key);
            PlayerPrefs.Save();
        }
    }
}
