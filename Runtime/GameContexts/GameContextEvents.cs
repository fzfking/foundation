namespace Foundation.GameContexts
{
    public readonly struct GameContextEnteredEvent
    {
        public GameContextId ContextId { get; }

        public string SaveNamespace { get; }

        public GameContextEnteredEvent(GameContextId contextId, string saveNamespace)
        {
            ContextId = contextId;
            SaveNamespace = saveNamespace;
        }
    }

    public readonly struct GameContextExitedEvent
    {
        public GameContextId ContextId { get; }

        public GameContextExitedEvent(GameContextId contextId)
        {
            ContextId = contextId;
        }
    }
}
