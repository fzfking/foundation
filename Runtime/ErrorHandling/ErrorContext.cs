using System;
using System.Collections.Generic;

namespace Foundation.ErrorHandling
{
    public sealed class ErrorContext
    {
        public string Code { get; }
        public string Message { get; }
        public ErrorSeverity Severity { get; }
        public Exception Exception { get; }
        public IReadOnlyDictionary<string, object> Parameters { get; }

        public ErrorContext(
            string code,
            string message,
            ErrorSeverity severity = ErrorSeverity.Error,
            Exception exception = null,
            IReadOnlyDictionary<string, object> parameters = null)
        {
            Code = string.IsNullOrWhiteSpace(code) ? throw new ArgumentException("Error code is required.", nameof(code)) : code;
            Message = message ?? string.Empty;
            Severity = severity;
            Exception = exception;
            Parameters = parameters;
        }
    }
}
