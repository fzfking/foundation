using System;
using Foundation.DI;

namespace Foundation.GameContexts
{
    public enum GameContextState
    {
        Created,
        Starting,
        Active,
        Stopping,
        Disposed,
    }

    /// <summary>
    /// A live game context: its definition plus the child DI scope that owns everything it created. Disposing
    /// it disposes the scope, which releases the context's state, simulation, features and scoped UI; the
    /// application scope is untouched.
    /// </summary>
    public sealed class GameContext : IDisposable
    {
        private readonly IServiceScope _scope;

        public GameContextDefinition Definition { get; }

        public GameContextId Id => Definition.Id;

        public string SaveNamespace => Definition.Save.Namespace;

        public string SaveSlotId => Definition.Save.SlotId;

        public GameContextState State { get; internal set; }

        public bool IsDisposed => State == GameContextState.Disposed;

        /// <summary>
        /// The context's own scope. Application-scope services stay resolvable from it, so context code can
        /// depend on them without knowing where they live.
        /// </summary>
        public IServiceScope Scope
        {
            get
            {
                if (IsDisposed)
                    throw new ObjectDisposedException(nameof(GameContext), $"Game context '{Id}' is already disposed.");

                return _scope;
            }
        }

        internal GameContext(GameContextDefinition definition, IServiceScope scope)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _scope = scope ?? throw new ArgumentNullException(nameof(scope));
            State = GameContextState.Created;
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            State = GameContextState.Disposed;
            _scope.Dispose();
        }

        public override string ToString() => $"{Id} [{State}]";
    }
}
