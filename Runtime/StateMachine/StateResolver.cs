using Foundation.DI;

namespace Foundation.StateMachine
{
    public interface IStateResolver<in TState> where TState : IState
    {
        TConcrete GetState<TConcrete>() where TConcrete : class, TState;
    }

    public class DiStateResolver<TState> : IStateResolver<TState> where TState : IState
    {
        private readonly IServiceResolver _resolver;

        public DiStateResolver(IServiceResolver resolver)
        {
            _resolver = resolver;
        }

        public TConcrete GetState<TConcrete>() where TConcrete : class, TState
        {
            return _resolver.Resolve<TConcrete>();
        }
    }
}
