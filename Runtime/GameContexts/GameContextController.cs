using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Foundation.Logging;
using Foundation.Messaging;
using Foundation.Save;
using Cysharp.Threading.Tasks;
using Foundation.DI;

namespace Foundation.GameContexts
{
    public interface IGameContextController
    {
        GameContext ActiveContext { get; }

        bool HasActiveContext { get; }

        /// <summary>Which context a fresh startup should enter: the persisted selection if it is
        /// still available in this build, otherwise the catalog's fallback (the main game).</summary>
        GameContextId ResolveStartupContextId();

        UniTask<GameContext> EnterStartupContextAsync(CancellationToken cancellationToken = default);

        UniTask<GameContext> EnterAsync(GameContextId contextId, CancellationToken cancellationToken = default);

        /// <summary>Exit and re-enter the active context, keeping its save.</summary>
        UniTask<GameContext> ReloadActiveAsync(CancellationToken cancellationToken = default);

        /// <summary>Discard the active context's save namespace and re-enter it from scratch.</summary>
        UniTask<GameContext> ResetActiveAsync(CancellationToken cancellationToken = default);

        UniTask ExitActiveAsync(CancellationToken cancellationToken = default);

        /// <summary>Stops and disposes the active context without saving its state.</summary>
        UniTask StopActiveAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Owns the lifecycle of the one active game context. Every transition runs the same pipeline: save the
    /// active namespace -> stop its runtime -> dispose its scope -> create the next scope -> start its
    /// runtime (which restores its own save).
    /// Saving before stopping is deliberate: the runtime is still alive, so the snapshot is the state the
    /// player left. A transition attempts recovery when starting a new context fails:
    /// - a save that fails, or a transition cancelled before its save completed, leaves the outgoing context
    /// running and the transition fails;
    /// - once saving completes, caller cancellation no longer interrupts teardown or entry into the next context;
    /// - after a failed entry, a switch tries the previous context then a distinct fallback; reload/reset only try
    /// the distinct fallback. The transition still fails. If all recovery candidates fail, there is no active context.
    /// Transitions are serialized: a second Enter/Reload/Reset waits for the first. A failing Entered/Exited
    /// subscriber is logged and never fails the transition.
    /// </summary>
    public sealed class GameContextController : IGameContextController, IDisposable
    {
        private const string LogCategory = "GameContext";

        private readonly IGameContextCatalog _catalog;
        private readonly IGameContextScopeFactory _scopeFactory;
        private readonly IGameContextSelectionStore _selectionStore;
        private readonly ISaveRepository _saveRepository;
        private readonly IEventBus _eventBus;
        private readonly IAppLogger _logger;
        private readonly SemaphoreSlim _transitionGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private bool _disposed;

        public GameContext ActiveContext { get; private set; }

        public bool HasActiveContext => ActiveContext is { IsDisposed: false };

        public GameContextController(
            IGameContextCatalog catalog,
            IGameContextScopeFactory scopeFactory,
            IGameContextSelectionStore selectionStore,
            ISaveRepository saveRepository = null,
            IEventBus eventBus = null,
            IAppLogger logger = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
            _saveRepository = saveRepository;
            _eventBus = eventBus;
            _logger = logger;
        }

        /// <summary>Synchronous emergency cleanup; call an async exit method when runtime quiescence is required.</summary>
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
                ActiveContext?.Dispose();
            }
            catch (Exception exception)
            {
                failure = CombineCleanupFailures(failure, exception);
            }
            finally
            {
                ActiveContext = null;
                try
                {
                    _lifetime.Dispose();
                }
                catch (Exception exception)
                {
                    failure = CombineCleanupFailures(failure, exception);
                }
            }

            // A transition still unwinding owns the gate until its finally block runs.
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        public GameContextId ResolveStartupContextId()
        {
            if (!_selectionStore.TryLoadLastActive(out var persisted))
                return _catalog.FallbackContextId;

            if (_catalog.Contains(persisted))
                return persisted;

            _logger?.Log(
                LogLevel.Warning,
                LogCategory,
                $"Last active game context '{persisted}' is not available in this build; falling back to '{_catalog.FallbackContextId}'.");
            return _catalog.FallbackContextId;
        }

        public UniTask<GameContext> EnterStartupContextAsync(CancellationToken cancellationToken = default)
            => EnterAsync(ResolveStartupContextId(), cancellationToken);

        public async UniTask<GameContext> EnterAsync(GameContextId contextId, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var definition = ResolveDefinition(contextId);
            var lifetimeToken = _lifetime.Token;
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
            cancellationToken = transition.Token;
            await AcquireTransitionGateAsync(cancellationToken);
            try
            {
                if (HasActiveContext && ActiveContext.Id == definition.Id)
                    return ActiveContext;

                var previous = HasActiveContext ? ActiveContext.Definition : null;
                await ExitActiveCoreAsync(persist: true, cancellationToken);
                return await EnterWithRecoveryAsync(definition, previous, lifetimeToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        public async UniTask<GameContext> ReloadActiveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var lifetimeToken = _lifetime.Token;
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
            cancellationToken = transition.Token;
            await AcquireTransitionGateAsync(cancellationToken);
            try
            {
                var definition = RequireActiveDefinition();
                await ExitActiveCoreAsync(persist: true, cancellationToken);
                return await EnterWithRecoveryAsync(definition, null, lifetimeToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        public async UniTask<GameContext> ResetActiveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var lifetimeToken = _lifetime.Token;
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken);
            cancellationToken = transition.Token;
            await AcquireTransitionGateAsync(cancellationToken);
            try
            {
                var definition = RequireActiveDefinition();
                // Later than every save of this context, or a cloud copy stamped by a clock that ran ahead would win
                // over the wipe on the next load.
                long? stamp = ActiveContext.Scope.TryResolve<SaveOrchestrator>(out var orchestrator)
                    ? orchestrator.NextStamp()
                    : null;
                // Saving on the way out would write this context's state straight back over the wipe.
                await ExitActiveCoreAsync(persist: false, cancellationToken);
                try
                {
                    await ClearSaveNamespaceAsync(definition.Save, stamp, lifetimeToken);
                }
                catch (Exception failure) when (!lifetimeToken.IsCancellationRequested)
                {
                    // The wipe did not happen: the player gets the context back as it was rather than no context.
                    var captured = ExceptionDispatchInfo.Capture(failure);
                    _logger?.Log(LogLevel.Error, LogCategory, $"Resetting game context '{definition.Id}' failed: {failure}");
                    await RecoverAsync(default, definition);
                    captured.Throw();
                }

                return await EnterWithRecoveryAsync(definition, null, lifetimeToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        public async UniTask ExitActiveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = transition.Token;
            await AcquireTransitionGateAsync(cancellationToken);
            try
            {
                await ExitActiveCoreAsync(persist: true, cancellationToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        public async UniTask StopActiveAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            using var transition = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = transition.Token;
            await AcquireTransitionGateAsync(cancellationToken);
            try
            {
                await ExitActiveCoreAsync(persist: false, cancellationToken);
            }
            finally
            {
                _transitionGate.Release();
            }
        }

        // Building a scope instantiates Unity objects, and a contended SemaphoreSlim continuation is not guaranteed
        // to resume on the main thread.
        private async UniTask AcquireTransitionGateAsync(CancellationToken cancellationToken)
        {
            await _transitionGate.WaitAsync(cancellationToken);
            try
            {
                await UniTask.SwitchToMainThread(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
            }
            catch
            {
                _transitionGate.Release();
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GameContextController));
        }

        private static Exception CombineCleanupFailures(Exception failure, Exception next)
            => failure == null ? next : new AggregateException(failure, next);

        private GameContextDefinition ResolveDefinition(GameContextId contextId)
        {
            if (!contextId.IsEmpty && _catalog.TryGet(contextId, out var definition))
                return definition;

            _logger?.Log(
                LogLevel.Warning,
                LogCategory,
                $"Game context '{contextId}' is not registered; falling back to '{_catalog.FallbackContextId}'.");
            return _catalog.Get(_catalog.FallbackContextId);
        }

        private GameContextDefinition RequireActiveDefinition()
            => HasActiveContext
                ? ActiveContext.Definition
                : throw new InvalidOperationException("There is no active game context.");

        // A cancelled entry is not recovered: the caller is going away.
        private async UniTask<GameContext> EnterWithRecoveryAsync(
            GameContextDefinition definition,
            GameContextDefinition previous,
            CancellationToken cancellationToken)
        {
            try
            {
                return await EnterCoreAsync(definition, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception failure)
            {
                var captured = ExceptionDispatchInfo.Capture(failure);
                _logger?.Log(LogLevel.Error, LogCategory, $"Game context '{definition.Id}' failed to start: {failure}");
                await RecoverAsync(definition.Id, previous);
                captured.Throw();
                throw;
            }
        }

        private async UniTask RecoverAsync(GameContextId failedId, GameContextDefinition previous)
        {
            if (_disposed)
                return;

            var candidates = new List<GameContextDefinition>(2);
            if (previous != null && previous.Id != failedId)
                candidates.Add(previous);
            if (_catalog.FallbackContextId != failedId
                && (previous == null || previous.Id != _catalog.FallbackContextId)
                && _catalog.TryGet(_catalog.FallbackContextId, out var fallback))
                candidates.Add(fallback);

            foreach (var candidate in candidates)
            {
                try
                {
                    await EnterCoreAsync(candidate, _lifetime.Token);
                    _logger?.Log(LogLevel.Warning, LogCategory, $"Recovered into game context '{candidate.Id}'.");
                    return;
                }
                catch (Exception exception)
                {
                    _logger?.Log(LogLevel.Error, LogCategory,
                        $"Recovering into game context '{candidate.Id}' failed too: {exception}");
                }
            }
        }

        private async UniTask<GameContext> EnterCoreAsync(
            GameContextDefinition definition,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var scope = _scopeFactory.CreateScope(definition);
            var context = new GameContext(definition, scope) { State = GameContextState.Starting };
            ActiveContext = context;

            try
            {
                var runtime = scope.Resolve<IGameContextRuntime>();
                await runtime.StartAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                context.State = GameContextState.Active;
                _selectionStore.SaveLastActive(definition.Id);
            }
            catch (Exception)
            {
                // Never leave a half-started context active; and never silently fall back, or a
                // broken context would look like a working main game.
                ActiveContext = null;
                try
                {
                    context.Dispose();
                }
                catch (Exception disposeException)
                {
                    try
                    {
                        _logger?.Log(LogLevel.Error, LogCategory,
                            $"Disposing failed game context '{definition.Id}' also failed: {disposeException}");
                    }
                    catch
                    {
                        // Preserve the original startup failure if cleanup diagnostics also fail.
                    }
                }
                throw;
            }

            _logger?.Log(
                LogLevel.Info,
                LogCategory,
                $"Entered game context '{definition.Id}' (save namespace '{definition.Save.Namespace}', slot '{definition.Save.SlotId}').");

            await PublishReportAsync(new GameContextEnteredEvent(definition.Id, definition.Save.Namespace));
            return context;
        }

        private async UniTask ExitActiveCoreAsync(bool persist, CancellationToken cancellationToken)
        {
            var context = ActiveContext;
            if (context == null || context.IsDisposed)
            {
                ActiveContext = null;
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var contextId = context.Id;
            if (persist)
            {
                // Until this save is through the context keeps running: a failed or cancelled save leaves the player
                // where they are with nothing lost, instead of throwing away what they played since the last save.
                try
                {
                    await SaveContextAsync(context, cancellationToken);
                }
                catch
                {
                    if (!context.IsDisposed)
                        context.State = GameContextState.Active;
                    throw;
                }
            }

            if (_disposed)
                cancellationToken.ThrowIfCancellationRequested();
            context.State = GameContextState.Stopping;
            if (context.Scope.TryResolve<IGameContextRuntime>(out var runtime))
            {
                try
                {
                    await runtime.StopAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // Best-effort: the scope is disposed next either way, and a failing teardown
                    // must not leave a zombie context active.
                    _logger?.Log(
                        LogLevel.Warning,
                        LogCategory,
                        $"Game context '{contextId}' failed to stop cleanly: {exception.Message}");
                }
            }

            Exception disposeFailure = null;
            try
            {
                context.Dispose();
            }
            catch (Exception exception)
            {
                disposeFailure = exception;
            }
            finally
            {
                ActiveContext = null;
            }

            await PublishReportAsync(new GameContextExitedEvent(contextId));
            if (disposeFailure != null)
                ExceptionDispatchInfo.Capture(disposeFailure).Throw();
        }

        private async UniTask SaveContextAsync(GameContext context, CancellationToken cancellationToken)
        {
            // No orchestrator means the context has nothing to persist - a valid shape, not an error.
            if (!context.Scope.TryResolve<SaveOrchestrator>(out var save))
                return;

            try
            {
                await save.SaveAsync(cancellationToken, context.SaveSlotId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger?.Log(
                    LogLevel.Error,
                    LogCategory,
                    $"Failed to save game context '{context.Id}' into slot '{context.SaveSlotId}'; it keeps running: {exception.Message}");
                throw;
            }
        }

        private async UniTask PublishReportAsync<TEvent>(TEvent report)
        {
            if (_disposed || _eventBus == null)
                return;

            try
            {
                await _eventBus.PublishAsync(report, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _logger?.Log(LogLevel.Error, LogCategory, $"A subscriber of {typeof(TEvent).Name} failed: {exception}");
            }
        }

        // An empty save restores no segments, so features keep their defaults, which is what a fresh context looks
        // like. It is an ordinary write, so cloud backends propagate the reset and no backend needs a delete.
        private async UniTask ClearSaveNamespaceAsync(
            GameContextSaveProfile save,
            long? stamp,
            CancellationToken cancellationToken)
        {
            if (_saveRepository == null)
                throw new InvalidOperationException(
                    $"Cannot reset game context save namespace '{save.Namespace}': no save repository was provided.");

            var empty = new SaveData(save.SaveVersion, new Dictionary<string, byte[]>(StringComparer.Ordinal), stamp);
            await _saveRepository.SaveAsync(empty, cancellationToken, save.SlotId);
            _logger?.Log(LogLevel.Info, LogCategory, $"Cleared save namespace '{save.Namespace}' (slot '{save.SlotId}').");
        }
    }
}
