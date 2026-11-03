using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Foundation.StateMachine
{
    public interface IStateMachine<in TState> where TState : IState
    {
        UniTask EnterAsync<TConcrete>() where TConcrete : class, TState;
        void Update();
        UniTask ExitAsync();
    }

    public class StateMachine<TState> : IStateMachine<TState> where TState : IState
    {
        private readonly IStateResolver<TState> _resolver;
        private readonly IStateTransitionValidator<TState> _validator;
        private readonly Queue<Func<UniTask>> _transitionQueue = new();
        private TState _currentState;
        private bool _isProcessingTransitionQueue;

        public StateMachine(IStateResolver<TState> resolver, IStateTransitionValidator<TState> validator)
        {
            _resolver = resolver;
            _validator = validator;
        }

        public UniTask EnterAsync<TConcrete>() where TConcrete : class, TState
            => EnqueueTransition(EnterStateAsync<TConcrete>);

        public void Update()
        {
            _currentState?.Update();
        }

        public UniTask ExitAsync() => EnqueueTransition(ExitCurrentStateAsync);

        private async UniTask EnterStateAsync<TConcrete>() where TConcrete : class, TState
        {
            if (!_validator.IsValid<TConcrete>(_currentState))
                return;

            var nextState = _resolver.GetState<TConcrete>();
            await ExitCurrentStateAsync();
            _currentState = nextState;
            try
            {
                await _currentState.EnterAsync();
            }
            catch
            {
                _currentState = default;
                try
                {
                    await nextState.ExitAsync();
                }
                catch (Exception)
                {
                    // Preserve the EnterAsync failure after best-effort cleanup.
                }

                throw;
            }
        }

        private async UniTask ExitCurrentStateAsync()
        {
            if (_currentState != null)
                await _currentState.ExitAsync();
            _currentState = default;
        }

        private UniTask EnqueueTransition(Func<UniTask> transition)
        {
            var completion = new UniTaskCompletionSource();
            _transitionQueue.Enqueue(() => ExecuteTransitionAsync(transition, completion));

            if (!_isProcessingTransitionQueue)
            {
                _isProcessingTransitionQueue = true;
                ProcessTransitionQueueAsync().Forget();
            }

            return completion.Task;
        }

        private async UniTask ExecuteTransitionAsync(Func<UniTask> transition, UniTaskCompletionSource completion)
        {
            try
            {
                await transition();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        private async UniTaskVoid ProcessTransitionQueueAsync()
        {
            try
            {
                while (_transitionQueue.Count > 0)
                {
                    var transition = _transitionQueue.Dequeue();
                    await transition();
                }
            }
            finally
            {
                _isProcessingTransitionQueue = false;
                if (_transitionQueue.Count > 0)
                {
                    _isProcessingTransitionQueue = true;
                    ProcessTransitionQueueAsync().Forget();
                }
            }
        }
    }
}
