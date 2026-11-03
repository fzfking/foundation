namespace Foundation.StateMachine
{
    public interface IStateTransitionValidator<in TState> where TState : IState
    {
        bool IsValid<TNext>(TState currentState) where TNext : TState;
    }

    public class AlwaysValidStateTransitionValidator<TState> : IStateTransitionValidator<TState> where TState : IState
    {
        public bool IsValid<TNext>(TState currentState) where TNext : TState => true;
    }
}
