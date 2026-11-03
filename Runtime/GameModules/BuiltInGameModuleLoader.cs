using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.GameModules
{
    /// <summary>
    /// Resolves modules from a compile-time-registered set of factories, the "no HybridCLR"
    /// <see cref="IGameModuleLoader"/>. It keeps the abstraction honest: a context works the same with this loader
    /// or <c>HybridClrGameModuleLoader</c>. A built-in module needs no download, so <see cref="LoadAsync"/>
    /// completes synchronously and the handle's <see cref="IDisposable.Dispose"/> is a no-op.
    /// </summary>
    public sealed class BuiltInGameModuleLoader : IGameModuleLoader
    {
        private readonly IReadOnlyDictionary<string, Func<IGameModule>> _factories;

        public BuiltInGameModuleLoader(IReadOnlyDictionary<GameModuleId, Func<IGameModule>> factories)
        {
            if (factories == null)
                throw new ArgumentNullException(nameof(factories));

            var byValue = new Dictionary<string, Func<IGameModule>>(factories.Count, StringComparer.Ordinal);
            foreach (var pair in factories)
                byValue[pair.Key.Value] = pair.Value;

            _factories = byValue;
        }

        public UniTask<IGameModuleHandle> LoadAsync(GameModuleId moduleId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_factories.TryGetValue(moduleId.Value, out var factory))
                throw new GameModuleLoadException(moduleId, "No built-in factory is registered for this module id.");

            IGameModule module;
            try
            {
                module = factory();
            }
            catch (Exception exception)
            {
                throw new GameModuleLoadException(moduleId, "Factory threw while constructing the module.", exception);
            }

            if (module == null)
                throw new GameModuleLoadException(moduleId, "Factory returned null.");

            if (!string.Equals(module.Id.Value, moduleId.Value, StringComparison.Ordinal))
            {
                throw new GameModuleLoadException(
                    moduleId,
                    $"Factory produced a module whose own Id ('{module.Id}') does not match the requested id.");
            }

            return UniTask.FromResult<IGameModuleHandle>(new Handle(module));
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
}
