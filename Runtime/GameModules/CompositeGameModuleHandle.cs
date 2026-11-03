using System;
using System.Collections.Generic;
using Foundation.DI;

namespace Foundation.GameModules
{
    /// <summary>
    /// Several loaded modules as one: configuring configures each in load order, disposing releases them in
    /// reverse order, once.
    /// </summary>
    public sealed class CompositeGameModuleHandle : IGameModuleHandle
    {
        private readonly IReadOnlyList<IGameModuleHandle> _handles;
        private bool _disposed;

        public IGameModule Module { get; }

        public CompositeGameModuleHandle(GameModuleId id, IReadOnlyList<IGameModuleHandle> handles)
        {
            _handles = handles ?? throw new ArgumentNullException(nameof(handles));
            Module = new CompositeModule(id, handles);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            DisposeAll(_handles);
        }

        /// <summary>Releases handles loaded so far, newest first - for a load that failed part way.</summary>
        public static void DisposeAll(IReadOnlyList<IGameModuleHandle> handles)
        {
            List<Exception> failures = null;
            for (var i = handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    handles[i].Dispose();
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>();
                    failures.Add(exception);
                }
            }

            if (failures != null)
                throw new AggregateException("One or more game module handles could not be released.", failures);
        }

        public static void EnsureIsModule(IGameModuleHandle handle, GameModuleId moduleId)
        {
            if (handle?.Module != null && handle.Module.Id == moduleId)
                return;

            handle?.Dispose();
            throw new GameModuleLoadException(moduleId, "The loader returned a different or empty module.");
        }

        private sealed class CompositeModule : IGameModule
        {
            private readonly IReadOnlyList<IGameModuleHandle> _handles;

            public GameModuleId Id { get; }

            public CompositeModule(GameModuleId id, IReadOnlyList<IGameModuleHandle> handles)
            {
                Id = id;
                _handles = handles;
            }

            public void Configure(IServiceRegistry registry)
            {
                foreach (var handle in _handles)
                    handle.Module.Configure(registry);
            }
        }
    }
}
