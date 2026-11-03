#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;

namespace Foundation.Diagnostics
{
    public interface IOperationTracer
    {
        bool IsEnabled(OperationTraceCategory category);

        IOperationTraceScope Begin(OperationTraceCategory category, string operation, string name);

        void Note(OperationTraceCategory category, string operation, string name);
    }

    public interface IOperationTraceScope : IDisposable
    {
        string CorrelationId { get; }

        string OperationId { get; }

        string ParentOperationId { get; }

        void Fail(Exception exception);

        void Suspend();

        void Resume();
    }
}
#endif
