#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Foundation.Logging;

namespace Foundation.Diagnostics
{
    public sealed class LoggingOperationTracer : IOperationTracer
    {
        private const string LogCategory = "Trace";

        private readonly IAppLogger _logger;
        private readonly OperationTraceOptions _options;
        private readonly List<Scope> _active = new();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _nextId;

        public LoggingOperationTracer(IAppLogger logger, OperationTraceOptions options = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options ?? new OperationTraceOptions();
        }

        public OperationTraceOptions Options => _options;

        public bool IsEnabled(OperationTraceCategory category) => _options.IsEnabled(category);

        public IOperationTraceScope Begin(OperationTraceCategory category, string operation, string name)
        {
            if (!_options.IsEnabled(category) || _options.IsMuted(name))
                return NullOperationTracer.Instance.Begin(category, operation, name);

            var parent = Current;
            var scope = new Scope(
                this,
                (++_nextId).ToString(CultureInfo.InvariantCulture),
                parent,
                _clock.Elapsed.TotalMilliseconds,
                operation,
                name);
            _active.Add(scope);
            Write(scope.Depth, scope.CorrelationId, $"> {operation} {name}");
            return scope;
        }

        public void Note(OperationTraceCategory category, string operation, string name)
        {
            if (!_options.IsEnabled(category) || _options.IsMuted(name))
                return;

            var parent = Current;
            Write(parent == null ? 0 : parent.Depth + 1, parent?.CorrelationId ?? "-", $". {operation} {name}");
        }

        private Scope Current => _active.Count > 0 ? _active[_active.Count - 1] : null;

        private void Activate(Scope scope)
        {
            if (!_active.Contains(scope))
                _active.Add(scope);
        }

        private void Deactivate(Scope scope)
        {
            var index = _active.LastIndexOf(scope);
            if (index >= 0)
                _active.RemoveRange(index, _active.Count - index);
        }

        private void Complete(Scope scope, Exception exception)
        {
            Deactivate(scope);

            var duration = _clock.Elapsed.TotalMilliseconds - scope.StartedAtMilliseconds;
            var suffix = duration.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
            if (exception == null)
            {
                Write(scope.Depth, scope.CorrelationId, $"< {scope.Operation} {scope.Name} ({suffix})");
                return;
            }

            Write(
                scope.Depth,
                scope.CorrelationId,
                $"! {scope.Operation} {scope.Name} ({suffix}) {exception.GetType().Name}: {exception.Message}");
        }

        private void Write(int depth, string correlationId, string message)
        {
            var builder = new StringBuilder();
            builder.Append('[').Append(correlationId).Append("] ");
            for (var index = 0; index < depth; index++)
                builder.Append("  ");
            builder.Append(message);
            _logger.Log(LogLevel.Debug, LogCategory, builder.ToString());
        }

        private sealed class Scope : IOperationTraceScope
        {
            private readonly LoggingOperationTracer _owner;
            private Exception _exception;
            private bool _completed;
            private bool _suspended;

            public Scope(
                LoggingOperationTracer owner,
                string operationId,
                Scope parent,
                double startedAtMilliseconds,
                string operation,
                string name)
            {
                _owner = owner;
                OperationId = operationId;
                CorrelationId = parent?.CorrelationId ?? operationId;
                ParentOperationId = parent?.OperationId;
                Depth = parent == null ? 0 : parent.Depth + 1;
                StartedAtMilliseconds = startedAtMilliseconds;
                Operation = operation;
                Name = name;
            }

            public string CorrelationId { get; }
            public string OperationId { get; }
            public string ParentOperationId { get; }
            public int Depth { get; }
            public double StartedAtMilliseconds { get; }
            public string Operation { get; }
            public string Name { get; }

            public void Fail(Exception exception) => _exception = exception;

            public void Suspend()
            {
                if (_completed || _suspended)
                    return;

                _suspended = true;
                _owner.Deactivate(this);
            }

            public void Resume()
            {
                if (_completed || !_suspended)
                    return;

                _suspended = false;
                _owner.Activate(this);
            }

            public void Dispose()
            {
                if (_completed)
                    return;

                _completed = true;
                _owner.Complete(this, _exception);
            }
        }
    }
}
#endif
