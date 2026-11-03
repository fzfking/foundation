using System;

namespace Foundation.DI
{
    /// <summary>
    /// Resolves services from one scope and its parents. The Foundation's view of "the container": a
    /// project's DI library is reached through this and <see cref="IServiceRegistry"/>, never directly.
    /// </summary>
    public interface IServiceResolver
    {
        /// <summary>Resolves a registered service; throws when none is registered.</summary>
        object Resolve(Type serviceType);

        /// <summary>Resolves a registered service; false when none is registered.</summary>
        bool TryResolve(Type serviceType, out object service);
    }

    public static class ServiceResolverExtensions
    {
        public static T Resolve<T>(this IServiceResolver resolver)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            return (T)resolver.Resolve(typeof(T));
        }

        public static bool TryResolve<T>(this IServiceResolver resolver, out T service)
        {
            if (resolver == null)
                throw new ArgumentNullException(nameof(resolver));

            if (resolver.TryResolve(typeof(T), out var resolved) && resolved is T typed)
            {
                service = typed;
                return true;
            }

            service = default;
            return false;
        }
    }
}
