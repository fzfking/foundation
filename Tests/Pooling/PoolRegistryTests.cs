using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Pooling;
using NUnit.Framework;

namespace Foundation.Tests.Pooling
{
    public sealed class PoolRegistryTests
    {
        [Test]
        public void LivePool_IsListedForDiagnostics()
        {
            var pool = CreatePool();

            var listed = new List<IPool>();
            PoolRegistry.Collect(listed);

            Assert.That(listed, Does.Contain(pool));
            pool.Dispose();
        }

        [Test]
        public void UndisposedPool_IsNotKeptAliveByTheRegistry()
        {
            var weak = CreateUndisposedPoolReference();

            for (var pass = 0; pass < 3; pass++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Assert.That(weak.TryGetTarget(out _), Is.False, "diagnostics must not extend a pool's lifetime");
        }

        private static Pool<object> CreatePool()
            => new Pool<object>(PoolId.From("registry-test"), new DelegatePoolStrategy<object>(() => new object()));

        // Created on a worker thread: a conservative collector (Mono in the Editor) can see a stale reference to the pool
        // in this thread's stack and keep it alive, which has nothing to do with the registry.
        private static WeakReference<IPool> CreateUndisposedPoolReference()
        {
            WeakReference<IPool> weak = null;
            var worker = new Thread(() => weak = new WeakReference<IPool>(CreatePool()));
            worker.Start();
            worker.Join();
            return weak;
        }
    }
}
