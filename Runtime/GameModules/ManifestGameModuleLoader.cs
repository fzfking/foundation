using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameModules
{
    /// <summary>
    /// Where the current <see cref="GameModuleManifest"/> comes from. A source may refresh it once (from a
    /// downloaded config) before the first module loads; <see cref="Current"/> is read again on every load.
    /// </summary>
    public interface IGameModuleManifestSource
    {
        GameModuleManifest Current { get; }

        UniTask EnsureRefreshedAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Loads a logical module and its declared dependencies dependency-first, returning one aggregate module
    /// whose Configure runs each exactly once. The dependency graph is read fresh on every call and refreshed
    /// before the first load, so overrides (a redirected address, a new module) are visible before the
    /// assembly loader is built. That loader (<paramref name="assemblyLoaderFactory"/>) is built lazily,
    /// once, from the address table current after that refresh.
    /// </summary>
    public sealed class ManifestGameModuleLoader : IGameModuleLoader, IDisposable
    {
        private readonly IGameModuleManifestSource _manifestSource;
        private readonly Func<IReadOnlyDictionary<GameModuleId, string>, IGameModuleLoader> _assemblyLoaderFactory;
        private readonly SemaphoreSlim _assemblyLoaderGate = new(1, 1);
        private IGameModuleLoader _assemblyLoader;

        public ManifestGameModuleLoader(
            IGameModuleManifestSource manifestSource,
            Func<IReadOnlyDictionary<GameModuleId, string>, IGameModuleLoader> assemblyLoaderFactory)
        {
            _manifestSource = manifestSource ?? throw new ArgumentNullException(nameof(manifestSource));
            _assemblyLoaderFactory = assemblyLoaderFactory ?? throw new ArgumentNullException(nameof(assemblyLoaderFactory));
        }

        /// <summary>
        /// Disposes the assembly loader (and its content handles) if one was built. The container calls this
        /// on scope disposal; the loader is built inside a factory closure, so nothing else would.
        /// </summary>
        public void Dispose()
        {
            (_assemblyLoader as IDisposable)?.Dispose();
        }

        public async UniTask<IGameModuleHandle> LoadAsync(
            GameModuleId moduleId,
            CancellationToken cancellationToken = default)
        {
            await _manifestSource.EnsureRefreshedAsync(cancellationToken);
            var assemblyLoader = await GetAssemblyLoaderAsync(cancellationToken);

            var handles = new List<IGameModuleHandle>();
            try
            {
                var loaded = new HashSet<GameModuleId>();
                await LoadDependencyTreeAsync(moduleId, assemblyLoader, loaded, handles, cancellationToken);
                return new CompositeGameModuleHandle(moduleId, handles);
            }
            catch (Exception loadFailure)
            {
                try
                {
                    CompositeGameModuleHandle.DisposeAll(handles);
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Module loading failed and one or more loaded modules could not be released.",
                        loadFailure, cleanupFailure);
                }

                throw;
            }
        }

        private async UniTask<IGameModuleLoader> GetAssemblyLoaderAsync(CancellationToken cancellationToken)
        {
            if (_assemblyLoader != null)
                return _assemblyLoader;

            await _assemblyLoaderGate.WaitAsync(cancellationToken);
            try
            {
                if (_assemblyLoader == null)
                {
                    var addresses = new Dictionary<GameModuleId, string>();
                    foreach (var entry in _manifestSource.Current.Entries)
                        addresses[entry.Id] = entry.AssemblyAddress;
                    _assemblyLoader = _assemblyLoaderFactory(addresses);
                }

                return _assemblyLoader;
            }
            finally
            {
                _assemblyLoaderGate.Release();
            }
        }

        private async UniTask LoadDependencyTreeAsync(
            GameModuleId moduleId,
            IGameModuleLoader assemblyLoader,
            HashSet<GameModuleId> loaded,
            List<IGameModuleHandle> handles,
            CancellationToken cancellationToken)
        {
            if (loaded.Contains(moduleId))
                return;

            var entry = _manifestSource.Current.Get(moduleId);
            foreach (var dependency in entry.Dependencies)
                await LoadDependencyTreeAsync(dependency, assemblyLoader, loaded, handles, cancellationToken);

            var handle = await assemblyLoader.LoadAsync(moduleId, cancellationToken);
            CompositeGameModuleHandle.EnsureIsModule(handle, moduleId);
            handles.Add(handle);
            loaded.Add(moduleId);
        }
    }
}
