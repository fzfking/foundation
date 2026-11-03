using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging
{
    public interface IEventBus
    {
        IDisposable Subscribe<TEvent>(IEventHandler<TEvent> handler);
        UniTask PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default);
    }
}
