using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Lifecycle
{
    public abstract class RootEntryPoint : IAsyncStartableEntryPoint
    {
        public async UniTask StartAsync(CancellationToken cancellation = new())
        {
            try
            {
                await RunAsync(cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // The lifetime scope was disposed underneath the startup sequence (leaving Play
                // Mode, or app/scene shutdown) — expected, not a failure the container should log.
            }
        }

        protected abstract UniTask RunAsync(CancellationToken cancellation);
    }
}
