using System;

namespace Foundation.TypedIds
{
    /// <summary>
    /// Marks a <c>partial struct</c> as a strongly typed stable id backed by a string key. The generator adds
    /// the serialized field, equality, ordering, debugger display and a MemoryPack formatter (when MemoryPack
    /// is referenced). <example><code>[StableId] public partial struct ScreenId { }</code></example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class StableIdAttribute : Attribute
    {
    }

    /// <summary>
    /// Marks a <c>partial struct</c> as a strongly typed runtime id backed by a <see cref="Guid"/>.
    /// <example><code>[RuntimeId] public partial struct SaveTransactionId { }</code></example>
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    public sealed class RuntimeIdAttribute : Attribute
    {
    }
}
