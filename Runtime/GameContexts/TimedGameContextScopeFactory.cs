#if UNITY_EDITOR || DEVELOPMENT_BUILD || FOUNDATION_DEVELOPMENT
using System.Diagnostics;
using Foundation.DI;
using Foundation.Logging;
using Unity.Profiling;
using System;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Measures how long building a context's scope blocks the main thread: its installers, the registry and the build
    /// callbacks (command handlers, entry points). Development builds and the Editor only. The numbers say whether building
    /// a scope on a background thread would be worth anything.
    /// </summary>
    public sealed class TimedGameContextScopeFactory : IGameContextScopeFactory
    {
        public const string DefaultMarkerName = "Foundation.ContextScope.Build";

        private readonly IGameContextScopeFactory _inner;
        private readonly ProfilerMarker _marker;
        private readonly IAppLogger _logger;

        /// <param name="markerName">The profiler marker the build is recorded under; a project names it after itself.</param>
        public TimedGameContextScopeFactory(IGameContextScopeFactory inner, string markerName = DefaultMarkerName, IAppLogger logger = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            _marker = new ProfilerMarker(string.IsNullOrWhiteSpace(markerName) ? DefaultMarkerName : markerName);
        }

        public IServiceScope CreateScope(GameContextDefinition definition)
        {
            var stopwatch = Stopwatch.StartNew();
            using (_marker.Auto())
            {
                var scope = _inner.CreateScope(definition);
                _logger.Log(LogLevel.Debug, "ScopeBuild", $"context '{definition.Id}': {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
                return scope;
            }
        }
    }
}
#endif
