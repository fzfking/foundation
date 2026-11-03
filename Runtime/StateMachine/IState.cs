using Cysharp.Threading.Tasks;

namespace Foundation.StateMachine
{
    public interface IState
    {
        UniTask EnterAsync();
        void Update();
        UniTask ExitAsync();
    }
}
