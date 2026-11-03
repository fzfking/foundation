using Cysharp.Threading.Tasks;

namespace Foundation.StateMachine
{
    public interface IStateMachineContext<in TState> where TState : IState
    {
        void RequestTransition<TConcrete>() where TConcrete : class, TState;
    }

    public class StateMachineContext<TState> : IStateMachineContext<TState> where TState : IState
    {
        private readonly IStateMachine<TState> _stateMachine;

        public StateMachineContext(IStateMachine<TState> stateMachine)
        {
            _stateMachine = stateMachine;
        }

        public void RequestTransition<TConcrete>() where TConcrete : class, TState
            => _stateMachine.EnterAsync<TConcrete>().Forget();
    }
}
