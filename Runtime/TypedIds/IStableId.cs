namespace Foundation.TypedIds
{
    /// <summary>
    /// A stable id of authored content (an audio event, a screen, a production). The permanent key saved,
    /// serialized in configs and referenced by other content, so it must not change with a display name.
    /// Implemented by structs generated from <see cref="StableIdAttribute"/>; never by hand.
    /// </summary>
    public interface IStableId
    {
        string Value { get; }
        bool IsEmpty { get; }
    }
}
