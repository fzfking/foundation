#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;

namespace Foundation.Diagnostics
{
    public sealed class NullOperationTracer : IOperationTracer
    {
        public static readonly NullOperationTracer Instance = new();

        public bool IsEnabled(OperationTraceCategory category) => false;

        public IOperationTraceScope Begin(OperationTraceCategory category, string operation, string name)
            => NullScope.Shared;

        public void Note(OperationTraceCategory category, string operation, string name)
        {
        }

        private sealed class NullScope : IOperationTraceScope
        {
            public static readonly NullScope Shared = new();

            public string CorrelationId => null;
            public string OperationId => null;
            public string ParentOperationId => null;

            public void Fail(Exception exception)
            {
            }

            public void Suspend()
            {
            }

            public void Resume()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
#endif
