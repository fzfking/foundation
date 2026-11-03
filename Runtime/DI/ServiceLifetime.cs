namespace Foundation.DI
{
    /// <summary>How long a registered service instance lives, mirrored by every container adapter.</summary>
    public enum ServiceLifetime
    {
        Transient,
        /// <summary>One instance per scope; a child scope gets its own.</summary>
        Scoped,
        /// <summary>One instance per scope that registered it, owned (and disposed) by that scope.</summary>
        Singleton,
    }
}
