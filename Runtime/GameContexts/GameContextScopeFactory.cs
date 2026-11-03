using Foundation.DI;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Builds a context's scope as a child of the application scope. A DI adapter implements it (see
    /// <c>VContainerGameContextScopeFactory</c>); the lifecycle relies on:
    /// <list type="bullet">
    /// <item>the child sees every application registration and its own registrations shadow the parent's,
    /// which is how a context gets its own config repository, save orchestrator or injector;</item>
    /// <item>a singleton registered in the child is owned and disposed by the child;</item>
    /// <item>entry points registered in the child start when it is built and stop when it is disposed.</item>
    /// </list>
    /// </summary>
    public interface IGameContextScopeFactory
    {
        IServiceScope CreateScope(GameContextDefinition definition);
    }
}
