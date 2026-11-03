using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Foundation.Spawning
{
    /// <summary>
    /// Spawns through Object.InstantiateAsync in local space: a copy keeps the original's local transform
    /// under its parent, like Object.Instantiate(original, parent, false). Unity integrates new objects in
    /// slices of <see cref="IntegrationTimeMS"/> per frame, so a burst cannot stall one frame. That slice is one
    /// setting for the whole process: a spawner built with a value applies it on each budgeted instantiation, and
    /// one built without leaves Unity's setting alone, so give the value to the single spawner the container shares.
    /// UI copies are synchronous, at most <see cref="UiCopiesPerFrame"/> per frame, so a burst of rows spreads over
    /// frames (see <see cref="InstantiateAsync{T}"/>).
    /// </summary>
    public sealed class AsyncObjectSpawner : IObjectSpawner
    {
        public const int DefaultIntegrationTimeMS = 2;
        public const int DefaultUiCopiesPerFrame = 2;

        private readonly int? _integrationTimeMs;
        private readonly int _uiCopiesPerFrame;
        private readonly UiCopyQueue _uiQueue = new();
        private bool _uiPumpRunning;

        public int? IntegrationTimeMS => _integrationTimeMs;
        public int UiCopiesPerFrame => _uiCopiesPerFrame;

        public AsyncObjectSpawner(int? integrationTimeMs = null, int uiCopiesPerFrame = DefaultUiCopiesPerFrame)
        {
            if (integrationTimeMs < 1)
                throw new ArgumentOutOfRangeException(nameof(integrationTimeMs));

            if (uiCopiesPerFrame < 1)
                throw new ArgumentOutOfRangeException(nameof(uiCopiesPerFrame));

            _integrationTimeMs = integrationTimeMs;
            _uiCopiesPerFrame = uiCopiesPerFrame;
        }

        internal static InstantiateParameters LocalPlacement(Transform parent) => new InstantiateParameters { parent = parent, worldSpace = false };

        // UUM-120045: asynchronous instantiation of a UI prefab can hand the canvas a copy still being
        // integrated (logs Invalid AABB and 'awake.loadStarted'). Still reproduces on 6000.5.11f1, so UI
        // copies are made synchronously, a few per frame. The interface stays asynchronous.
        public UniTask<T> InstantiateAsync<T>(
            T original,
            Transform parent = null,
            CancellationToken cancellationToken = default)
            where T : Object
        {
            return IsUi(original)
                ? EnqueueUiCopy(original, parent, cancellationToken)
                : InstantiateBudgetedAsync(original, parent, cancellationToken);
        }

        private UniTask<T> EnqueueUiCopy<T>(T original, Transform parent, CancellationToken cancellationToken)
            where T : Object
        {
            if (cancellationToken.IsCancellationRequested)
                return UniTask.FromCanceled<T>(cancellationToken);

            var completion = new UniTaskCompletionSource<T>();
            var hadParent = parent != null;
            _uiQueue.Enqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                    return false;
                }
                
                if (hadParent && parent == null)
                {
                    completion.TrySetCanceled(CancellationToken.None);
                    return false;
                }

                try
                {
                    completion.TrySetResult(Object.Instantiate(original, parent, false));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }

                return true;
            });

            if (!_uiPumpRunning)
            {
                _uiPumpRunning = true;
                PumpUiQueueAsync().Forget();
            }

            return completion.Task;
        }

        private async UniTaskVoid PumpUiQueueAsync()
        {
            try
            {
                do
                {
                    await UniTask.Yield();
                    _uiQueue.Process(_uiCopiesPerFrame);
                }
                while (_uiQueue.Count > 0);
            }
            finally
            {
                _uiPumpRunning = false;
            }
        }

        private async UniTask<T> InstantiateBudgetedAsync<T>(
            T original,
            Transform parent,
            CancellationToken cancellationToken)
            where T : Object
        {
            if (original == null)
                throw new ArgumentNullException(nameof(original));

            cancellationToken.ThrowIfCancellationRequested();
            
            if (_integrationTimeMs is { } integrationTimeMs)
                AsyncInstantiateOperation.SetIntegrationTimeMS(integrationTimeMs);
            
            var operation = parent == null
                ? Object.InstantiateAsync(original)
                : Object.InstantiateAsync(original, LocalPlacement(parent), default);

            // Unity cannot abort an instantiation that has started. Wait, then honour the cancellation by
            // destroying what was made, so nothing is left without an owner.
            // ReSharper disable once MethodSupportsCancellation
            await operation.ToUniTask();

            var instances = operation.Result;
            if (instances == null || instances.Length == 0 || instances[0] == null)
                throw new InvalidOperationException($"Instantiating '{original.name}' produced no object.");

            var instance = instances[0];
            if (cancellationToken.IsCancellationRequested)
            {
                Object.Destroy(instance);
                throw new OperationCanceledException(cancellationToken);
            }

            return instance;
        }

        private static bool IsUi(Object original)
        {
            var root = original is GameObject gameObject ? gameObject : original is Component component ? component.gameObject : null;
            return root != null && root.TryGetComponent<RectTransform>(out _);
        }
    }
}
