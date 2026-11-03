using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    /// <summary>
    /// Lets one operation run at a time and hands the turn to the waiting ones in the order they asked. Main thread only,
    /// like the buses that use it.
    /// </summary>
    internal sealed class SerialExecutionGate
    {
        private readonly LinkedList<UniTaskCompletionSource> _waiting = new();
        private readonly object _sync = new();
        private bool _busy;

        public UniTask EnterAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waiter = new UniTaskCompletionSource();
            LinkedListNode<UniTaskCompletionSource> node;
            lock (_sync)
            {
                if (!_busy)
                {
                    _busy = true;
                    return UniTask.CompletedTask;
                }

                node = _waiting.AddLast(waiter);
            }

            return WaitAsync(waiter, node, cancellationToken);
        }

        public void Exit()
        {
            UniTaskCompletionSource next;
            lock (_sync)
            {
                if (_waiting.First == null)
                {
                    _busy = false;
                    return;
                }

                // Keep the gate busy while handing the turn over, so a new operation cannot jump the queue.
                next = _waiting.First.Value;
                _waiting.RemoveFirst();
            }

            next.TrySetResult();
        }

        private async UniTask WaitAsync(UniTaskCompletionSource waiter, LinkedListNode<UniTaskCompletionSource> node, CancellationToken cancellationToken)
        {
            await using var registration = cancellationToken.CanBeCanceled
                ? cancellationToken.Register(() => Abandon(waiter, node, cancellationToken))
                : default;
            await waiter.Task;
        }

        private void Abandon(UniTaskCompletionSource waiter, LinkedListNode<UniTaskCompletionSource> node, CancellationToken cancellationToken)
        {
            // Only a waiter still in the queue gives up; one that already got the turn goes on and releases it itself.
            var canceled = false;
            lock (_sync)
            {
                if (node.List != null)
                {
                    _waiting.Remove(node);
                    canceled = true;
                }
            }

            if (canceled)
                waiter.TrySetCanceled(cancellationToken);
        }
    }
}
