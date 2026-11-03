using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Foundation.DI;
using Foundation.GameModules;
using Foundation.Logging;
using Cysharp.Threading.Tasks;

namespace Foundation.GameContexts
{
    /// <summary>Contexts described after the build was made (by a downloaded config), joining the catalog before an enter.</summary>
    public interface IGameContextDescriptorSource
    {
        IReadOnlyList<GameContextDescriptor> ContextDescriptors { get; }

        UniTask EnsureRefreshedAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// The time a context's availability window is judged by. Implement it over a server time so the player
    /// cannot open a season early by moving the device clock; a failure makes the window count as closed.
    /// </summary>
    public interface IGameContextClock
    {
        UniTask<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>The device clock: the default, which the player can move.</summary>
    public sealed class DeviceGameContextClock : IGameContextClock
    {
        public UniTask<DateTimeOffset> GetUtcNowAsync(CancellationToken cancellationToken = default)
            => UniTask.FromResult(DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The project's own steps around a context switch of <see cref="ModularGameContextController"/>: making
    /// content present, preparing the runtime to load modules, presentation that outlives one context.
    /// </summary>
    public interface IGameContextTransitionHooks
    {
        /// <summary>Before the context's modules load: whatever they need to load (content, runtime metadata).</summary>
        UniTask PrepareAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken);

        /// <summary>Modules are loaded and the outgoing context still runs: the last step before the switch.</summary>
        UniTask BeforeSwitchAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken);

        void Entered(GameContext context);

        /// <summary>The switch failed; <paramref name="active"/> is the context running now, or null when none is.</summary>
        UniTask SwitchFailedAsync(GameContext active);

        void Exited();
    }

    /// <summary>
    /// A context controller whose contexts live in loadable modules. Entering refreshes the described
    /// contexts, refuses a context outside its availability window (the fallback is entered), loads its
    /// modules, publishes its definition and lets <see cref="GameContextController"/> run the switch. Module
    /// handles of the running context are kept until another context runs; a failed switch releases exactly
    /// the modules no running context uses. This controller owns the inner controller and its module handles.
    /// </summary>
    public sealed class ModularGameContextController : IGameContextController, IDisposable
    {
        private const string LogCategory = "GameContexts";

        private readonly GameContextController _inner;
        private readonly MaterializedGameContextCatalog _catalog;
        private readonly IGameModuleLoader _moduleLoader;
        private readonly IGameContextDescriptorSource _descriptorSource;
        private readonly IGameContextTransitionHooks _hooks;
        private readonly IAppLogger _logger;
        private readonly IGameContextClock _clock;
        private readonly bool _resumeLastActive;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _lifetimeToken;
        private IGameModuleHandle _activeModules;
        private GameContextId _activeModuleContextId;
        private IGameModuleHandle _pendingModules;
        private GameContextId _pendingModuleContextId;
        private volatile bool _disposed;
        private bool _resourcesDisposed;

        public GameContext ActiveContext => _inner.ActiveContext;

        public bool HasActiveContext => _inner.HasActiveContext;

        /// <param name="resumeLastActive">Whether a startup enters the context last played; otherwise always the fallback.</param>
        /// <param name="clock">The clock availability windows are judged by; the device clock by default.</param>
        /// <param name="inner">The inner controller whose lifetime transfers to this wrapper.</param>
        public ModularGameContextController(
            GameContextController inner,
            MaterializedGameContextCatalog catalog,
            IGameModuleLoader moduleLoader,
            IGameContextDescriptorSource descriptorSource = null,
            IGameContextTransitionHooks hooks = null,
            IAppLogger logger = null,
            bool resumeLastActive = true,
            IGameContextClock clock = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _moduleLoader = moduleLoader ?? throw new ArgumentNullException(nameof(moduleLoader));
            _descriptorSource = descriptorSource;
            _hooks = hooks ?? NoHooks.Instance;
            _logger = logger;
            _resumeLastActive = resumeLastActive;
            _clock = clock ?? new DeviceGameContextClock();
            _lifetimeToken = _lifetime.Token;
        }

        /// <summary>Cancels in-flight transitions and disposes the owned inner controller and module handles.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Exception failure = null;
            try
            {
                _lifetime.Cancel();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            try
            {
                _inner.Dispose();
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailures(failure, exception);
            }

            if (_gate.Wait(0))
            {
                try
                {
                    DisposeOwnedResources(ref failure);
                }
                finally
                {
                    _gate.Release();
                }
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public GameContextId ResolveStartupContextId()
            => _resumeLastActive ? _inner.ResolveStartupContextId() : _catalog.FallbackContextId;

        public UniTask<GameContext> EnterStartupContextAsync(CancellationToken cancellationToken = default)
            => EnterAsync(ResolveStartupContextId(), cancellationToken);

        public async UniTask<GameContext> EnterAsync(GameContextId contextId, CancellationToken cancellationToken = default)
        {
            await AcquireGateAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
                cancellationToken = transition.Token;
                if (HasActiveContext && ActiveContext.Id == contextId)
                    return ActiveContext;

                var descriptor = await ResolveDescriptorAsync(contextId, cancellationToken);
                await _hooks.PrepareAsync(descriptor, cancellationToken);
                var loadedModules = await LoadModulesAsync(descriptor, cancellationToken);
                _pendingModules = loadedModules;
                if (_disposed)
                    throw new ObjectDisposedException(nameof(ModularGameContextController));

                _pendingModuleContextId = descriptor.Id;
                try
                {
                    _catalog.Materialize(CreateDefinition(descriptor, loadedModules));
                }
                catch
                {
                    if (!_disposed)
                    {
                        _pendingModules = null;
                        _pendingModuleContextId = default;
                        loadedModules.Dispose();
                    }
                    throw;
                }

                var previousModules = _activeModules;
                var previousContextId = _activeModuleContextId;
                try
                {
                    await _hooks.BeforeSwitchAsync(descriptor, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    ThrowIfDisposed();
                    var context = await _inner.EnterAsync(descriptor.Id, cancellationToken);
                    ThrowIfDisposed();
                    Keep(descriptor.Id, loadedModules, previousContextId, previousModules);
                    _hooks.Entered(context);
                    return context;
                }
                catch
                {
                    if (_disposed)
                        throw;

                    var active = HasActiveContext ? ActiveContext : null;
                    if (active != null && active.Id == descriptor.Id)
                    {
                        if (!ReferenceEquals(_activeModules, loadedModules))
                            Keep(descriptor.Id, loadedModules, previousContextId, previousModules);
                    }
                    else if (active != null && active.Id == previousContextId)
                        Release(descriptor.Id, loadedModules, previousContextId, previousModules);
                    else
                        ReleaseAll(descriptor.Id, loadedModules, previousContextId, previousModules);

                    await _hooks.SwitchFailedAsync(active);
                    throw;
                }
            }
            finally
            {
                ReleaseOperationGate();
            }
        }

        public async UniTask<GameContext> ReloadActiveAsync(CancellationToken cancellationToken = default)
        {
            await AcquireGateAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
                var context = await _inner.ReloadActiveAsync(transition.Token);
                ThrowIfDisposed();
                return context;
            }
            catch
            {
                if (!_disposed)
                    ReleaseModulesIfContextStopped();
                throw;
            }
            finally
            {
                ReleaseOperationGate();
            }
        }

        public async UniTask<GameContext> ResetActiveAsync(CancellationToken cancellationToken = default)
        {
            await AcquireGateAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
                var context = await _inner.ResetActiveAsync(transition.Token);
                ThrowIfDisposed();
                return context;
            }
            catch
            {
                if (!_disposed)
                    ReleaseModulesIfContextStopped();
                throw;
            }
            finally
            {
                ReleaseOperationGate();
            }
        }

        public async UniTask ExitActiveAsync(CancellationToken cancellationToken = default)
        {
            await AcquireGateAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
                await _inner.ExitActiveAsync(transition.Token);
                ThrowIfDisposed();
                ReleaseModulesIfContextStopped();
            }
            catch
            {
                if (!_disposed)
                    ReleaseModulesIfContextStopped();
                throw;
            }
            finally
            {
                ReleaseOperationGate();
            }
        }

        public async UniTask StopActiveAsync(CancellationToken cancellationToken = default)
        {
            await AcquireGateAsync(cancellationToken);
            try
            {
                ThrowIfDisposed();
                using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
                await _inner.StopActiveAsync(transition.Token);
                ThrowIfDisposed();
                ReleaseModulesIfContextStopped();
            }
            catch
            {
                if (!_disposed)
                    ReleaseModulesIfContextStopped();
                throw;
            }
            finally
            {
                ReleaseOperationGate();
            }
        }

        private async UniTask<GameContextDescriptor> ResolveDescriptorAsync(
            GameContextId contextId,
            CancellationToken cancellationToken)
        {
            // Contexts described by a downloaded config are not in the compiled-in list; they join the catalog once the
            // config is resolved, before the id is looked up. A described context never replaces a compiled-in one.
            if (_descriptorSource != null)
            {
                await _descriptorSource.EnsureRefreshedAsync(cancellationToken);
                foreach (var described in _descriptorSource.ContextDescriptors)
                {
                    if (!_catalog.TryAddDescriptor(described)
                        && !_catalog.TryGetDescriptor(described.Id, out _))
                    {
                        _logger?.Log(LogLevel.Warning, LogCategory,
                            $"Context '{described.Id}' was ignored because save slot '{described.Save.SlotId}' is already assigned to another context.");
                    }
                }
            }

            var descriptor = _catalog.TryGetDescriptor(contextId, out var requested)
                ? requested
                : _catalog.GetDescriptor(_catalog.FallbackContextId);
            // A season that has not opened yet or is over is not entered, whoever asked for it.
            if (!descriptor.Availability.IsLimited)
                return descriptor;

            if (await IsOpenAsync(descriptor, cancellationToken))
                return descriptor;

            _logger?.Log(LogLevel.Warning, LogCategory,
                $"Context '{descriptor.Id}' is outside its availability window; entering '{_catalog.FallbackContextId}' instead.");
            return _catalog.GetDescriptor(_catalog.FallbackContextId);
        }

        private async UniTask<bool> IsOpenAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken)
        {
            try
            {
                return descriptor.Availability.IsOpenAt(await _clock.GetUtcNowAsync(cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Warning, LogCategory,
                    $"The clock for context '{descriptor.Id}' failed ({exception.Message}); its window counts as closed.");
                return false;
            }
        }

        private async UniTask<IGameModuleHandle> LoadModulesAsync(
            GameContextDescriptor descriptor,
            CancellationToken cancellationToken)
        {
            var handles = new List<IGameModuleHandle>(descriptor.Modules.Count);
            try
            {
                foreach (var moduleId in descriptor.Modules)
                {
                    var handle = await _moduleLoader.LoadAsync(moduleId, cancellationToken);
                    CompositeGameModuleHandle.EnsureIsModule(handle, moduleId);
                    handles.Add(handle);
                }

                return new CompositeGameModuleHandle(descriptor.Modules[0], handles);
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

        // The incoming context runs: its modules stay, the outgoing context's go.
        private void Keep(
            GameContextId contextId,
            IGameModuleHandle modules,
            GameContextId previousContextId,
            IGameModuleHandle previousModules)
        {
            _activeModules = modules;
            _activeModuleContextId = contextId;
            if (ReferenceEquals(_pendingModules, modules))
            {
                _pendingModules = null;
                _pendingModuleContextId = default;
            }

            Exception failure = null;
            if (!ReferenceEquals(previousModules, modules))
                CaptureCleanupFailure(ref failure, () => previousModules?.Dispose());
            if (!previousContextId.IsEmpty && previousContextId != contextId)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(previousContextId));
            ThrowCleanupFailure(failure);
        }

        // The outgoing context still (or again) runs: the incoming context's modules go.
        private void Release(
            GameContextId contextId,
            IGameModuleHandle modules,
            GameContextId previousContextId,
            IGameModuleHandle previousModules)
        {
            if (ReferenceEquals(_pendingModules, modules))
            {
                _pendingModules = null;
                _pendingModuleContextId = default;
            }

            Exception failure = null;
            if (!ReferenceEquals(previousModules, modules))
                CaptureCleanupFailure(ref failure, modules.Dispose);
            if (contextId != previousContextId)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(contextId));
            ThrowCleanupFailure(failure);
        }

        // Nothing runs: every module goes.
        private void ReleaseAll(
            GameContextId contextId,
            IGameModuleHandle modules,
            GameContextId previousContextId,
            IGameModuleHandle previousModules)
        {
            _pendingModules = null;
            _pendingModuleContextId = default;
            _activeModules = null;
            var activeModuleContextId = _activeModuleContextId;
            _activeModuleContextId = default;

            Exception failure = null;
            CaptureCleanupFailure(ref failure, modules.Dispose);
            if (!ReferenceEquals(previousModules, modules))
                CaptureCleanupFailure(ref failure, () => previousModules?.Dispose());
            CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(contextId));
            if (!previousContextId.IsEmpty)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(previousContextId));
            if (!activeModuleContextId.IsEmpty
                && activeModuleContextId != contextId
                && activeModuleContextId != previousContextId)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(activeModuleContextId));
            CaptureCleanupFailure(ref failure, _hooks.Exited);
            ThrowCleanupFailure(failure);
        }

        private void ReleaseModulesIfContextStopped()
        {
            if (HasActiveContext)
                return;

            var modules = _activeModules;
            var contextId = _activeModuleContextId;
            _activeModules = null;
            _activeModuleContextId = default;

            Exception failure = null;
            CaptureCleanupFailure(ref failure, _hooks.Exited);
            CaptureCleanupFailure(ref failure, () => modules?.Dispose());
            if (!contextId.IsEmpty)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(contextId));
            ThrowCleanupFailure(failure);
        }

        private void ReleaseOperationGate()
        {
            try
            {
                if (_disposed)
                {
                    Exception failure = null;
                    DisposeOwnedResources(ref failure);
                    ThrowCleanupFailure(failure);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        private void DisposeOwnedResources(ref Exception failure)
        {
            if (_resourcesDisposed)
                return;

            _resourcesDisposed = true;
            var activeModules = _activeModules;
            var activeContextId = _activeModuleContextId;
            var pendingModules = _pendingModules;
            var pendingContextId = _pendingModuleContextId;
            _activeModules = null;
            _activeModuleContextId = default;
            _pendingModules = null;
            _pendingModuleContextId = default;

            CaptureCleanupFailure(ref failure, _hooks.Exited);
            CaptureCleanupFailure(ref failure, () => activeModules?.Dispose());
            if (!ReferenceEquals(pendingModules, activeModules))
                CaptureCleanupFailure(ref failure, () => pendingModules?.Dispose());
            if (!activeContextId.IsEmpty)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(activeContextId));
            if (!pendingContextId.IsEmpty && pendingContextId != activeContextId)
                CaptureCleanupFailure(ref failure, () => _catalog.Dematerialize(pendingContextId));
            CaptureCleanupFailure(ref failure, _lifetime.Dispose);
        }

        private static void CaptureCleanupFailure(ref Exception failure, Action cleanup)
        {
            if (cleanup == null)
                return;

            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailures(failure, exception);
            }
        }

        private static void ThrowCleanupFailure(Exception failure)
        {
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static Exception CombineCleanupFailures(Exception failure, Exception next)
            => failure == null ? next : new AggregateException(failure, next);

        private async UniTask AcquireGateAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.SwitchToMainThread(cancellationToken);
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ModularGameContextController));
        }

        private static GameContextDefinition CreateDefinition(GameContextDescriptor descriptor, IGameModuleHandle modules)
        {
            var installers = new IFeatureInstaller[]
            {
                new SeasonInstaller(new GameContextSeason(descriptor.SeasonId, descriptor.Availability)),
                new GameModuleFeatureInstaller(modules.Module),
            };
            return new GameContextDefinition(descriptor.Id, descriptor.Save, installers, descriptor.DisplayName);
        }

        // Every context's scope says which season it is (none for a context that is not seasonal), so a seasonal event's
        // logic, shared by all of its seasons, can tell them apart.
        private sealed class SeasonInstaller : IFeatureInstaller
        {
            private readonly GameContextSeason _season;

            public SeasonInstaller(GameContextSeason season) => _season = season;

            public void Install(IServiceRegistry registry) => registry.RegisterInstance(_season);
        }

        private sealed class NoHooks : IGameContextTransitionHooks
        {
            public static readonly NoHooks Instance = new();

            public UniTask PrepareAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public UniTask BeforeSwitchAsync(GameContextDescriptor descriptor, CancellationToken cancellationToken)
                => UniTask.CompletedTask;

            public void Entered(GameContext context)
            {
            }

            public UniTask SwitchFailedAsync(GameContext active) => UniTask.CompletedTask;

            public void Exited()
            {
            }
        }
    }
}
