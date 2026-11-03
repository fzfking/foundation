using System;

namespace Foundation.TypedIds
{
    public interface IGuidGenerator
    {
        Guid Next();
    }

    /// <summary>Creates random ids. Thread-safe. Inject <see cref="IGuidGenerator"/> so tests can supply fixed ids.</summary>
    public sealed class GuidGenerator : IGuidGenerator
    {
        public Guid Next() => Guid.NewGuid();
    }
}
