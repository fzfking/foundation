using System;
using Foundation.DI;
using VContainer;

namespace Foundation.Infrastructure.VContainer
{
    public static class VContainerRegistryExtensions
    {
        /// <summary>
        /// The VContainer builder behind <paramref name="registry"/>, for registrations that need VContainer's full API
        /// (parameters, components, factories). Throws when the registry is not the VContainer adapter.
        /// </summary>
        public static IContainerBuilder ToContainerBuilder(this IServiceRegistry registry)
        {
            if (registry is VContainerServiceRegistry vcontainer)
                return vcontainer.Builder;

            throw new InvalidOperationException(
                $"{registry?.GetType().Name ?? "null"} is not a VContainer registry; this code needs the VContainer adapter.");
        }
    }
}
