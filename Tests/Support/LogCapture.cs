using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Tests.Support
{
    // Takes Unity's log output for the scope of a test whose subject is expected to log, so the expected entries are asserted
    // instead of landing in the console as errors that look like failures.
    public sealed class LogCapture : IDisposable
    {
        private readonly ILogHandler _previous;
        private readonly Handler _handler = new();

        public LogCapture()
        {
            _previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = _handler;
        }

        public IReadOnlyList<string> Entries => _handler.Entries;

        public bool Contains(LogType type, string text)
        {
            var prefix = type + ": ";
            foreach (var entry in _handler.Entries)
            {
                if (entry.StartsWith(prefix, StringComparison.Ordinal) && entry.Contains(text))
                    return true;
            }

            return false;
        }

        public void Dispose() => Debug.unityLogger.logHandler = _previous;

        private sealed class Handler : ILogHandler
        {
            public readonly List<string> Entries = new();

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
                => Entries.Add(logType + ": " + string.Format(format, args));

            public void LogException(Exception exception, Object context)
                => Entries.Add(LogType.Exception + ": " + exception.GetType().Name + ": " + exception.Message);
        }
    }
}
