using UnityEngine;

namespace Foundation.DI
{
    /// <summary>
    /// Gives a freshly created GameObject (a popup, a pooled instance) the services of its scope, so an
    /// instance created for a game context sees that context's services.
    /// </summary>
    public interface IGameObjectInjector
    {
        void Inject(GameObject instance);
    }

    /// <summary>
    /// A component that wants the scope's resolver once, right after creation. The injector hands it over for
    /// every such component under an injected GameObject, whatever injection attributes the container has.
    /// </summary>
    public interface IServiceResolverClient
    {
        void Bind(IServiceResolver resolver);
    }
}
