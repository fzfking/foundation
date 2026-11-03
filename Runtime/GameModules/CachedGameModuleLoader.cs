using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameModules
{
    /// <summary>
    /// Keeps each loaded module resident for the process lifetime. Community HybridCLR cannot unload
    /// assemblies, so the cache is static and survives root-scope recreation after save deletion or a context
    /// restart.
    /// </summary>
    public sealed class CachedGameModuleLoader : IGameModuleLoader, IDisposable
    {
        // Static on purpose, and not a service: a hot-update assembly cannot be unloaded with HybridCLR
        // (community), so it must never be loaded twice (that raises an assembly error). Do not make this
        // per-scope and do not clear it.
        private static readonly object ProcessSync = new();
        private static readonly Dictionary<GameModuleId, IGameModule> ProcessModules = new();
        private static readonly Dictionary<GameModuleId, SemaphoreSlim> ProcessGates = new();

        private readonly IGameModuleLoader _inner;
        private readonly Dictionary<GameModuleId, IGameModule> _loaded = new();
        private readonly List<IGameModuleHandle> _ownedHandles = new();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _sync = new();
        private bool _disposed;

        public CachedGameModuleLoader(IGameModuleLoader inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public void Dispose()
        {
            List<IGameModuleHandle> handles;
            lock (_sync)
            {
                if (_disposed)
                    return;

                _disposed = true;
                handles = new(_ownedHandles);
                _ownedHandles.Clear();
                _loaded.Clear();
            }

            CompositeGameModuleHandle.DisposeAll(handles);
        }

        public async UniTask<IGameModuleHandle> LoadAsync(
            GameModuleId moduleId,
            CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                SemaphoreSlim processGate;
                lock (ProcessSync)
                {
                    if (!ProcessGates.TryGetValue(moduleId, out processGate))
                    {
                        processGate = new SemaphoreSlim(1, 1);
                        ProcessGates.Add(moduleId, processGate);
                    }
                }

                await processGate.WaitAsync(cancellationToken);
                try
                {
                    return await LoadOnceAsync(moduleId, cancellationToken);
                }
                finally
                {
                    processGate.Release();
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private async UniTask<IGameModuleHandle> LoadOnceAsync(
            GameModuleId moduleId,
            CancellationToken cancellationToken)
        {
            IGameModule module;
            lock (_sync)
            {
                ThrowIfDisposed();
                if (_loaded.TryGetValue(moduleId, out module))
                    return new Lease(module);
            }

            lock (ProcessSync)
            {
                if (ProcessModules.TryGetValue(moduleId, out module))
                {
                    lock (_sync)
                    {
                        ThrowIfDisposed();
                        _loaded.Add(moduleId, module);
                    }

                    return new Lease(module);
                }
            }

            var handle = await _inner.LoadAsync(moduleId, cancellationToken);
            if (handle?.Module == null || handle.Module.Id != moduleId)
            {
                handle?.Dispose();
                throw new InvalidOperationException($"Loader returned an invalid module for '{moduleId}'.");
            }

            module = handle.Module;
            lock (ProcessSync)
            {
                ProcessModules.Add(moduleId, module);
                lock (_sync)
                {
                    if (_disposed)
                    {
                        handle.Dispose();
                        throw new ObjectDisposedException(nameof(CachedGameModuleLoader));
                    }

                    _ownedHandles.Add(handle);
                    _loaded.Add(moduleId, module);
                }
            }

            return new Lease(module);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CachedGameModuleLoader));
        }

        private sealed class Lease : IGameModuleHandle
        {
            public IGameModule Module { get; }

            public Lease(IGameModule module) => Module = module;

            public void Dispose()
            {
            }
        }
    }
}
