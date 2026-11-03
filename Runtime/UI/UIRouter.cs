using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    public interface IUIRouter : IDisposable
    {
        UniTask<IUIView> OpenAsync(UiDefinition definition, UiArgs args = default, CancellationToken cancellationToken = default);
        UniTask<TView> OpenAsync<TView>(UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView;
        UniTask<TView> OpenAsync<TView>(UiDefinition definition, UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView;
        UniTask<IUIView> ReplaceAsync(UiDefinition definition, UiArgs args = default, CancellationToken cancellationToken = default);
        UniTask<TView> ReplaceAsync<TView>(UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView;
        UniTask<TView> ReplaceAsync<TView>(UiDefinition definition, UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView;
        UniTask CloseAsync(IUIView view, CancellationToken cancellationToken = default);
        UniTask<bool> BackAsync(CancellationToken cancellationToken = default);
        UniTask ClearAsync(CancellationToken cancellationToken = default);
        bool HasOpenViews(UILayer layer);
    }

    public sealed class UIRouter : IUIRouter
    {
        private readonly IUIDefinitionRegistry _registry;
        private readonly IUIViewFactory _viewFactory;
        private readonly List<UIStackEntry> _stack = new();
        private readonly Dictionary<string, UIStackEntry> _cache = new();
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly CancellationToken _lifetimeToken;
        private readonly HashSet<UIStackEntry> _ownedEntries = new();
        private bool _disposed;

        public UIRouter(IUIDefinitionRegistry registry, IUIViewFactory viewFactory)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _viewFactory = viewFactory ?? throw new ArgumentNullException(nameof(viewFactory));
            _lifetimeToken = _lifetime.Token;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            List<Exception> failures = null;
            CaptureCleanupFailure(ref failures, _lifetime.Cancel);
            var entries = new List<UIStackEntry>(_ownedEntries);
            _ownedEntries.Clear();
            _stack.Clear();
            _cache.Clear();
            foreach (var entry in entries)
                CaptureCleanupFailure(ref failures, () => ThrowCleanupFailure(entry.RequestDispose()));
            CaptureCleanupFailure(ref failures, _lifetime.Dispose);
            // In-flight operations still release the gate in their finally blocks.
            ThrowCleanupFailures(failures, "One or more views could not be released by the UI router.");
        }

        public UniTask<TView> OpenAsync<TView>(UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView
        {
            var definition = _registry.Resolve<TView>();
            return OpenTypedAsync<TView>(definition, args, cancellationToken);
        }

        public UniTask<TView> OpenAsync<TView>(UiDefinition definition, UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView
            => OpenTypedAsync<TView>(definition, args, cancellationToken);

        public UniTask<IUIView> OpenAsync(
            UiDefinition definition,
            UiArgs args = default,
            CancellationToken cancellationToken = default)
            => OpenInternalAsync(definition, args, cancellationToken, null);

        private async UniTask<TView> OpenTypedAsync<TView>(
            UiDefinition definition,
            UiArgs args,
            CancellationToken cancellationToken)
            where TView : class, IUIView
            => (TView)await OpenInternalAsync(definition, args, cancellationToken, typeof(TView));

        private async UniTask<IUIView> OpenInternalAsync(
            UiDefinition definition,
            UiArgs args,
            CancellationToken cancellationToken,
            Type expectedViewType)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            ThrowIfDisposed();
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                for (var i = 0; i < _stack.Count; i++)
                {
                    var existing = _stack[i];
                    if (!definition.AllowDuplicates && existing.Definition.ViewId == definition.ViewId)
                    {
                        EnsureViewType(existing.View, definition, expectedViewType);
                        ApplyArgs(existing.View, args);
                        await existing.ShowAsync(cancellationToken);
                        // A successful show commits the open; only router disposal can still abort it.
                        _lifetimeToken.ThrowIfCancellationRequested();
                        return existing.View;
                    }
                }

                if (!_cache.TryGetValue(definition.ViewId, out var entry))
                    entry = await CreateEntryAsync(definition, cancellationToken, expectedViewType);
                else
                    EnsureViewType(entry.View, definition, expectedViewType);

                try
                {
                    _stack.Add(entry);
                    ApplyArgs(entry.View, args);
                    await entry.ShowAsync(cancellationToken);
                    // A successful show commits the open; only router disposal can still abort it.
                    _lifetimeToken.ThrowIfCancellationRequested();
                    return entry.View;
                }
                catch
                {
                    ReleaseEntry(entry);
                    throw;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public UniTask<TView> ReplaceAsync<TView>(UiArgs args = default, CancellationToken cancellationToken = default) where TView : class, IUIView
        {
            var definition = _registry.Resolve<TView>();
            return ReplaceTypedAsync<TView>(definition, args, cancellationToken);
        }

        public UniTask<TView> ReplaceAsync<TView>(
            UiDefinition definition,
            UiArgs args = default,
            CancellationToken cancellationToken = default) where TView : class, IUIView
            => ReplaceTypedAsync<TView>(definition, args, cancellationToken);

        public UniTask<IUIView> ReplaceAsync(
            UiDefinition definition,
            UiArgs args = default,
            CancellationToken cancellationToken = default)
            => ReplaceInternalAsync(definition, args, cancellationToken, null);

        private async UniTask<TView> ReplaceTypedAsync<TView>(
            UiDefinition definition,
            UiArgs args,
            CancellationToken cancellationToken)
            where TView : class, IUIView
            => (TView)await ReplaceInternalAsync(definition, args, cancellationToken, typeof(TView));

        private async UniTask<IUIView> ReplaceInternalAsync(
            UiDefinition definition,
            UiArgs args,
            CancellationToken cancellationToken,
            Type expectedViewType)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            ThrowIfDisposed();
            var callerCancellationToken = cancellationToken;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                var target = await GetOrCreateEntryAsync(definition, cancellationToken, expectedViewType);
                var wasOpen = _stack.Contains(target);
                var targetCommitted = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ApplyArgs(target.View, args);
                    await target.ShowAsync(cancellationToken);
                    // A successful show commits the target; only router disposal can still abort it.
                    _lifetimeToken.ThrowIfCancellationRequested();

                    _stack.Remove(target);
                    _stack.Add(target);
                    targetCommitted = true;

                    // A caller cancellation stops the remaining outgoing hides; the committed target stays.
                    for (var i = _stack.Count - 1; i >= 0; i--)
                    {
                        var entry = _stack[i];
                        if (!ReferenceEquals(entry, target))
                            await CloseEntryAsync(entry, cancellationToken, callerCancellationToken);
                    }

                    return target.View;
                }
                catch
                {
                    if (!wasOpen && !targetCommitted)
                        ReleaseEntry(target);
                    throw;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async UniTask CloseAsync(IUIView view, CancellationToken cancellationToken = default)
        {
            if (view == null)
                throw new ArgumentNullException(nameof(view));
            ThrowIfDisposed();
            var callerCancellationToken = cancellationToken;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                var index = FindStackIndex(view);
                if (index < 0)
                    return;
                var entry = _stack[index];
                await CloseEntryAsync(entry, cancellationToken, callerCancellationToken);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public async UniTask<bool> BackAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var callerCancellationToken = cancellationToken;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            UIStackEntry top;
            IUIBackHandler handler;
            CancellationTokenSource backOperation = null;
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                if (_stack.Count == 0)
                    return false;
                top = _stack[_stack.Count - 1];
                handler = top.View as IUIBackHandler;
                if (handler == null)
                {
                    if (!top.Definition.AllowBack)
                        return false;
                    await CloseEntryAsync(top, cancellationToken);
                    return true;
                }

                backOperation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken);
                top.RegisterBackOperation(backOperation);
            }
            finally
            {
                _operationGate.Release();
            }

            // Outside the gate: the view may open or close views itself. The entry owns this lease until the handler exits.
            Exception failure = null;
            var handled = false;
            try
            {
                handled = await handler.HandleBackAsync(backOperation.Token);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                var cleanupFailure = top.EndBackOperation(backOperation);
                backOperation.Dispose();
                if (cleanupFailure != null)
                    failure = failure == null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
            }

            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return handled;
        }

        public async UniTask ClearAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var callerCancellationToken = cancellationToken;
            using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            cancellationToken = operation.Token;
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
                while (_stack.Count > 0)
                    await CloseEntryAsync(_stack[_stack.Count - 1], cancellationToken, callerCancellationToken);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        public bool HasOpenViews(UILayer layer)
        {
            for (var i = 0; i < _stack.Count; i++)
                if (_stack[i].Definition.Layer == layer)
                    return true;
            return false;
        }

        private async UniTask<UIStackEntry> GetOrCreateEntryAsync(
            UiDefinition definition,
            CancellationToken cancellationToken,
            Type expectedViewType)
        {
            if (_cache.TryGetValue(definition.ViewId, out var cachedEntry))
            {
                EnsureViewType(cachedEntry.View, definition, expectedViewType);
                return cachedEntry;
            }

            return await CreateEntryAsync(definition, cancellationToken, expectedViewType);
        }

        private async UniTask<UIStackEntry> CreateEntryAsync(
            UiDefinition definition,
            CancellationToken cancellationToken,
            Type expectedViewType = null)
        {
            var instance = await _viewFactory.CreateAsync(definition, cancellationToken);
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                instance?.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfDisposed();
            }

            if (instance?.View == null)
            {
                instance?.Dispose();
                throw new InvalidOperationException($"UI definition '{definition.ViewId}' produced no view.");
            }

            try
            {
                EnsureViewType(instance.View, definition, expectedViewType);
            }
            catch
            {
                instance.Dispose();
                throw;
            }

            var entry = new UIStackEntry(definition, instance.View, instance);
            _ownedEntries.Add(entry);
            if (definition.CachePolicy == UICachePolicy.KeepAlive)
                _cache.Add(definition.ViewId, entry);
            return entry;
        }

        private async UniTask CloseEntryAsync(
            UIStackEntry entry,
            CancellationToken cancellationToken,
            CancellationToken initiatingToken = default)
        {
            // Until hide succeeds the router retains ownership and the caller can retry a cancelled close.
            cancellationToken.ThrowIfCancellationRequested();
            await entry.HideAsync(cancellationToken);
            // A successful hide commits the close; only router disposal can still abort it.
            _lifetimeToken.ThrowIfCancellationRequested();
            _stack.Remove(entry);
            if (entry.Definition.CachePolicy == UICachePolicy.DestroyOnClose)
                ReleaseEntry(entry, initiatingToken);
            else
                ThrowCleanupFailure(entry.CancelBackOperations(initiatingToken));
        }

        private void ReleaseEntry(UIStackEntry entry, CancellationToken initiatingToken = default)
        {
            _stack.Remove(entry);
            if (_cache.TryGetValue(entry.Definition.ViewId, out var cached) && ReferenceEquals(cached, entry))
                _cache.Remove(entry.Definition.ViewId);
            if (_ownedEntries.Remove(entry))
                ThrowCleanupFailure(entry.RequestDispose(initiatingToken));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(UIRouter));
        }

        private static void CaptureCleanupFailure(ref List<Exception> failures, Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception)
            {
                (failures ??= new List<Exception>()).Add(exception);
            }
        }

        private static void ThrowCleanupFailure(Exception failure)
        {
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void ThrowCleanupFailures(List<Exception> failures, string message)
        {
            if (failures == null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException(message, failures);
        }

        private int FindStackIndex(IUIView view)
        {
            for (var i = 0; i < _stack.Count; i++) if (ReferenceEquals(_stack[i].View, view)) return i;
            return -1;
        }

        private static void ApplyArgs(IUIView view, UiArgs args)
        {
            if (view is IUIArgsReceiver receiver)
                receiver.SetArgs(args);
        }

        private static void EnsureViewType(IUIView view, UiDefinition definition, Type expectedViewType)
        {
            if (expectedViewType != null && !expectedViewType.IsInstanceOfType(view))
                throw new InvalidOperationException(
                    $"UI definition '{definition.ViewId}' produced '{view.GetType().FullName}', expected '{expectedViewType.FullName}'.");
        }
    }
}
