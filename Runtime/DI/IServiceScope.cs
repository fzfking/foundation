using System;

namespace Foundation.DI
{
    /// <summary>
    /// A container scope that can be left: disposing it disposes every <see cref="IDisposable"/> singleton it
    /// owns and unhooks its entry points. A child sees its parent's registrations, never the reverse.
    /// </summary>
    public interface IServiceScope : IServiceResolver, IDisposable
    {
        /// <summary>Creates a child scope; <paramref name="configure"/> registers what only the child owns.</summary>
        IServiceScope CreateScope(Action<IServiceRegistry> configure = null);
    }
}
