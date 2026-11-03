#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using Foundation.Diagnostics;
using Cysharp.Threading.Tasks;

namespace Foundation.Messaging.Tracing
{
    internal static class TracedOperation
    {
        public static async UniTask RunAsync(IOperationTraceScope scope, Func<UniTask> call)
        {
            try
            {
                var inner = call();
                if (inner.Status.IsCompleted())
                {
                    await inner;
                    return;
                }

                scope.Suspend();
                try
                {
                    await inner;
                }
                finally
                {
                    scope.Resume();
                }
            }
            catch (Exception exception)
            {
                scope.Fail(exception);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }

        public static async UniTask<TResult> RunAsync<TResult>(
            IOperationTraceScope scope,
            Func<UniTask<TResult>> call)
        {
            try
            {
                var inner = call();
                if (inner.Status.IsCompleted())
                    return await inner;

                scope.Suspend();
                try
                {
                    return await inner;
                }
                finally
                {
                    scope.Resume();
                }
            }
            catch (Exception exception)
            {
                scope.Fail(exception);
                throw;
            }
            finally
            {
                scope.Dispose();
            }
        }
    }
}
#endif
