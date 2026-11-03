using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Foundation.Content;
using Foundation.GameModules;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Infrastructure.HybridClr
{
    /// <summary>
    /// Maps a <see cref="GameModuleId"/> to a HybridCLR hot-update assembly delivered as a TextAsset
    /// (<c>.dll</c> bytes with a <c>.bytes</c> extension) through <see cref="IAssetProvider"/>, not a
    /// parallel download path. The caller supplies each module's address (
    /// <see cref="HybridClrModuleSource"/>); no naming convention here. The only reflection in the module chain:
    /// <see cref="Assembly.Load(byte[])"/>, one <c>Type</c> scan for the single <see cref="IGameModule"/>,
    /// <see cref="Activator.CreateInstance(Type)"/>.
    /// </summary>
    public sealed class HybridClrGameModuleLoader : IGameModuleLoader
    {
        private static readonly object AssemblySync = new();
        private static readonly Dictionary<GameModuleId, string> ModuleHashes = new();
        private static readonly Dictionary<string, Assembly> LoadedAssemblies = new(StringComparer.Ordinal);

        private readonly IAssetProvider _assetProvider;
        private readonly IReadOnlyDictionary<string, string> _assemblyAddressByModuleId;
        private readonly IHotUpdateAssemblyVerifier _verifier;
        private readonly IHotUpdateAssemblySource _assemblySource;

        public HybridClrGameModuleLoader(
            IAssetProvider assetProvider,
            IReadOnlyCollection<HybridClrModuleSource> sources,
            IHotUpdateAssemblyVerifier verifier = null,
            IHotUpdateAssemblySource assemblySource = null)
        {
            // Without a verifier, any loadable assembly at the address executes without an authenticity check.
            _verifier = verifier;
            _assemblySource = assemblySource;
            _assetProvider = assetProvider ?? throw new ArgumentNullException(nameof(assetProvider));

            if (sources == null)
                throw new ArgumentNullException(nameof(sources));

            var byId = new Dictionary<string, string>(sources.Count, StringComparer.Ordinal);
            foreach (var source in sources)
            {
                if (byId.ContainsKey(source.ModuleId.Value))
                    throw new ArgumentException($"Duplicate module id '{source.ModuleId}'.", nameof(sources));

                byId.Add(source.ModuleId.Value, source.AssemblyAddress);
            }

            _assemblyAddressByModuleId = byId;
        }

        public async UniTask<IGameModuleHandle> LoadAsync(GameModuleId moduleId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_assemblyAddressByModuleId.TryGetValue(moduleId.Value, out var assemblyAddress))
                throw new GameModuleLoadException(moduleId, "No hot-update assembly address is registered for this module id.");

            byte[] dllBytes;
            try
            {
                if (_assemblySource != null)
                {
                    dllBytes = await _assemblySource.LoadVerifiedAsync(assemblyAddress, cancellationToken);
                }
                else
                {
                    // Release the bundle before Assembly.Load so a later invalid sibling can be fetched from another mirror.
                    using var assemblyHandle = await _assetProvider.LoadAssetAsync<TextAsset>(assemblyAddress, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    dllBytes = _verifier == null
                        ? assemblyHandle.Asset.bytes
                        : _verifier.Verify(assemblyAddress, assemblyHandle.Asset.bytes);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HotUpdateSignatureException exception)
            {
                throw new GameModuleLoadException(moduleId, exception.Message, exception);
            }
            catch (Exception exception)
            {
                throw new GameModuleLoadException(
                    moduleId,
                    $"Failed to load or verify hot-update assembly bytes from '{assemblyAddress}'.",
                    exception);
            }

            cancellationToken.ThrowIfCancellationRequested();

            Assembly assembly;
            try
            {
                assembly = LoadAssembly(moduleId, dllBytes, cancellationToken);
            }
            catch (GameModuleLoadException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new GameModuleLoadException(moduleId, $"Assembly.Load failed for '{assemblyAddress}'.", exception);
            }

            cancellationToken.ThrowIfCancellationRequested();

            var module = CreateModule(moduleId, assembly);
            return new Handle(module);
        }

        private static Assembly LoadAssembly(GameModuleId moduleId, byte[] bytes, CancellationToken cancellationToken)
        {
            // The same direct construction as the signature check: SHA256.Create() looks the algorithm up by name, which
            // managed code stripping can break in a player build.
            var hash = Convert.ToBase64String(HotUpdateSignature.Hash(bytes));

            lock (AssemblySync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ModuleHashes.TryGetValue(moduleId, out var existingHash) && existingHash != hash)
                    throw new GameModuleLoadException(moduleId, "A different assembly is already loaded for this module. Restart the process before loading another version.");

                if (LoadedAssemblies.TryGetValue(hash, out var loaded))
                {
                    ModuleHashes[moduleId] = hash;
                    return loaded;
                }

                var assembly = Assembly.Load(bytes);
                LoadedAssemblies.Add(hash, assembly);
                ModuleHashes[moduleId] = hash;
                return assembly;
            }
        }

        private static IGameModule CreateModule(GameModuleId moduleId, Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                // A type whose dependency is missing: report it as this module's failure rather than a raw reflection error.
                throw new GameModuleLoadException(moduleId, $"Assembly '{assembly.GetName().Name}' has types that cannot be loaded.", exception);
            }

            Type moduleType = null;
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface || !typeof(IGameModule).IsAssignableFrom(type))
                    continue;

                if (moduleType != null)
                {
                    throw new GameModuleLoadException(
                        moduleId,
                        $"Assembly '{assembly.GetName().Name}' defines more than one IGameModule ('{moduleType.FullName}' and '{type.FullName}') - exactly one entry point per hot-update assembly is expected.");
                }

                moduleType = type;
            }

            if (moduleType == null)
                throw new GameModuleLoadException(moduleId, $"Assembly '{assembly.GetName().Name}' defines no IGameModule implementation.");

            IGameModule module;
            try
            {
                module = (IGameModule)Activator.CreateInstance(moduleType);
            }
            catch (Exception exception)
            {
                throw new GameModuleLoadException(moduleId, $"Failed to construct module entry type '{moduleType.FullName}'.", exception);
            }

            if (!string.Equals(module.Id.Value, moduleId.Value, StringComparison.Ordinal))
            {
                throw new GameModuleLoadException(
                    moduleId,
                    $"Loaded module reports Id '{module.Id}', which does not match the requested id.");
            }

            return module;
        }

        private sealed class Handle : IGameModuleHandle
        {
            public IGameModule Module { get; }

            public Handle(IGameModule module) => Module = module;

            public void Dispose()
            {
            }
        }
    }

    /// <summary>Where one hot-update module's compiled assembly bytes live, addressed through the
    /// existing content pipeline.</summary>
    public readonly struct HybridClrModuleSource
    {
        public GameModuleId ModuleId { get; }

        public string AssemblyAddress { get; }

        public HybridClrModuleSource(GameModuleId moduleId, string assemblyAddress)
        {
            ModuleId = moduleId;
            AssemblyAddress = string.IsNullOrWhiteSpace(assemblyAddress)
                ? throw new ArgumentException("Assembly address must not be empty.", nameof(assemblyAddress))
                : assemblyAddress;
        }
    }
}
