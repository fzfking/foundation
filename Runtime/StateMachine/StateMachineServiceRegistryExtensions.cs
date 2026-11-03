using System;
using Foundation.DI;

namespace Foundation.StateMachine
{
    public static class StateMachineServiceRegistryExtensions
    {
        /// <summary>
        /// A state machine for <typeparamref name="TState"/> that allows every transition. The states themselves are
        /// registered by the caller; the machine resolves them from the scope.
        /// </summary>
        public static IServiceRegistry UseStateMachine<TState>(this IServiceRegistry registry)
            where TState : IState
            => registry.UseStateMachine<TState, AlwaysValidStateTransitionValidator<TState>>();

        /// <summary>A state machine for <typeparamref name="TState"/> whose transitions <typeparamref name="TValidator"/> approves.</summary>
        public static IServiceRegistry UseStateMachine<TState, TValidator>(this IServiceRegistry registry)
            where TState : IState
            where TValidator : class, IStateTransitionValidator<TState>
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<DiStateResolver<TState>>(ServiceLifetime.Singleton).As<IStateResolver<TState>>();
            registry.Register<TValidator>(ServiceLifetime.Singleton).As<IStateTransitionValidator<TState>>();
            registry.Register<StateMachine<TState>>(ServiceLifetime.Singleton).As<IStateMachine<TState>>();
            registry.Register<StateMachineContext<TState>>(ServiceLifetime.Singleton).As<IStateMachineContext<TState>>();
            return registry;
        }
    }
}
