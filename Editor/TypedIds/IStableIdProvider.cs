using System;
using System.Collections.Generic;

namespace Foundation.TypedIds.Editor
{
    /// <summary>
    /// Tells the editor which definitions exist for one stable id type. Found automatically; needs a public
    /// parameterless constructor and must be stateless, as one instance is reused.
    /// </summary>
    public interface IStableIdProvider
    {
        Type IdType { get; }

        void Collect(List<StableIdEntry> entries);
    }
}
