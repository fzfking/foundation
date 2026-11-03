namespace Foundation.Release
{
    public enum ReleaseSelectionKind
    {
        Unknown,
        Current,
        Previous,
        Incompatible,
    }

    /// <summary>
    /// Which release of an environment this client may use. For
    /// <see cref="ReleaseSelectionKind.Incompatible"/> the slot is the newest published release the client does not fit;
    /// for <see cref="ReleaseSelectionKind.Unknown"/> it is null.
    /// </summary>
    public readonly struct ReleaseSelection
    {
        public ReleaseSelectionKind Kind { get; }

        public ReleaseSlot Slot { get; }

        public bool CanServe => Kind is ReleaseSelectionKind.Current or ReleaseSelectionKind.Previous;

        public ReleaseSelection(ReleaseSelectionKind kind, ReleaseSlot slot)
        {
            Kind = kind;
            Slot = slot;
        }
    }
}
