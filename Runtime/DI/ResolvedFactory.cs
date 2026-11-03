using System;

namespace Foundation.DI
{
    public abstract class ResolvedFactory<T> : IFactory<T>
    {
        protected readonly IServiceResolver Resolver;

        protected ResolvedFactory(IServiceResolver resolver)
        {
            Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public abstract T Create();
    }

    public abstract class ResolvedFactory<TParam, T> : IFactory<TParam, T>
    {
        protected readonly IServiceResolver Resolver;

        protected ResolvedFactory(IServiceResolver resolver)
        {
            Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public abstract T Create(TParam param);
    }
}
