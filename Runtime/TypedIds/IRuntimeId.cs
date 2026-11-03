using System;

namespace Foundation.TypedIds
{
    /// <summary>
    /// The id of something created while the game runs (an entity, a save transaction, a network record).
    /// Backed by a <see cref="Guid"/>. Never for authored content: that is <see cref="IStableId"/>.
    /// </summary>
    public interface IRuntimeId
    {
        Guid Value { get; }
        bool IsEmpty { get; }
    }
}
