using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.UI
{
    public sealed class UIStackEntry
    {
        private readonly IDisposable _instance;
        private readonly object _gate = new();
        private readonly List<CancellationTokenSource> _backOperations = new();
        private int _lifecycleOperations;
        private bool _disposeRequested;
        private bool _instanceDisposed;

        public UiDefinition Definition { get; }
        public IUIView View { get; }

        internal UIStackEntry(UiDefinition definition, IUIView view, IDisposable instance)
        {
            Definition = definition;
            View = view;
            _instance = instance;
        }

        internal void RegisterBackOperation(CancellationTokenSource operation)
        {
            lock (_gate)
            {
                if (_disposeRequested)
                    throw new ObjectDisposedException(nameof(UIStackEntry));

                _backOperations.Add(operation);
            }
        }

        internal Exception CancelBackOperations(CancellationToken except = default)
        {
            List<Exception> failures = null;
            List<CancellationTokenSource> operations;
            lock (_gate)
                operations = new List<CancellationTokenSource>(_backOperations);

            foreach (var operation in operations)
            {
                try
                {
                    if (operation.Token == except)
                        continue;

                    operation.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // The handler completed after this operation snapshot was taken.
                }
                catch (Exception exception)
                {
                    (failures ??= new List<Exception>()).Add(exception);
                }
            }

            return failures == null ? null : new AggregateException("One or more UI back handlers failed while being canceled.", failures);
        }

        internal Exception RequestDispose(CancellationToken except = default)
        {
            lock (_gate)
                _disposeRequested = true;

            var failures = CancelBackOperations(except);
            var disposeFailure = DisposeIfRequested();
            if (disposeFailure != null)
                failures = failures == null ? disposeFailure : new AggregateException(failures, disposeFailure);
            return failures;
        }

        internal Exception EndBackOperation(CancellationTokenSource operation)
        {
            lock (_gate)
                _backOperations.Remove(operation);

            return DisposeIfRequested();
        }

        internal UniTask ShowAsync(CancellationToken cancellationToken)
            => RunLifecycleAsync(() => View.ShowAsync(cancellationToken));

        internal UniTask HideAsync(CancellationToken cancellationToken)
            => RunLifecycleAsync(() => View.HideAsync(cancellationToken));

        private async UniTask RunLifecycleAsync(Func<UniTask> invoke)
        {
            lock (_gate)
            {
                if (_disposeRequested)
                    throw new ObjectDisposedException(nameof(UIStackEntry));

                _lifecycleOperations++;
            }

            Exception failure = null;
            try
            {
                await invoke();
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            var cleanupFailure = EndLifecycleOperation();
            if (cleanupFailure != null)
                failure = failure == null ? cleanupFailure : new AggregateException(failure, cleanupFailure);
            if (failure != null)
                ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private Exception EndLifecycleOperation()
        {
            lock (_gate)
                _lifecycleOperations--;

            return DisposeIfRequested();
        }

        private Exception DisposeIfRequested()
        {
            lock (_gate)
            {
                // shortcut: a non-cooperative view callback retains its instance; add a timeout contract if callbacks can stall.
                if (!_disposeRequested || _instanceDisposed || _backOperations.Count != 0 || _lifecycleOperations != 0)
                    return null;

                _instanceDisposed = true;
            }

            try
            {
                _instance.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
}
