using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Firebase;
using UnityEngine;

namespace Foundation.GameFramework.Firebase
{
    /// <summary>
    /// The one place that brings the Firebase app up: checks (and on Android repairs) Google Play services,
    /// then creates the default app. Analytics and Crashlytics wait on it, so it happens once per process and
    /// a failure is reported once.
    /// Firebase collects at launch unless the manifest turns collection off
    /// (<c>firebase_analytics_collection_enabled</c>, <c>firebase_crashlytics_collection_enabled</c>); when
    /// to switch it on is the project's consent decision.
    /// </summary>
    public static class FirebaseActivationCore
    {
        // Each caller gets its own waiter: a UniTask may be awaited only once while it is pending, and several modules
        // ask for the activation at the same time.
        private static readonly List<UniTaskCompletionSource<bool>> Waiters = new();
        private static bool? _result;
        private static bool _started;

        /// <summary>Completes with true once the Firebase app is usable; false when the device or the project configuration cannot run it.</summary>
        public static UniTask<bool> EnsureReadyAsync()
        {
            if (_result.HasValue)
                return UniTask.FromResult(_result.Value);

            var waiter = new UniTaskCompletionSource<bool>();
            Waiters.Add(waiter);
            if (!_started)
            {
                _started = true;
                RunAsync().Forget();
            }

            return waiter.Task;
        }

        private static async UniTaskVoid RunAsync()
        {
            var ready = await ActivateAsync();
            _result = ready;
            foreach (var waiter in Waiters)
                waiter.TrySetResult(ready);
            Waiters.Clear();
        }

        private static async UniTask<bool> ActivateAsync()
        {
            try
            {
                var status = await FirebaseApp.CheckAndFixDependenciesAsync();
                // Firebase completes its tasks off the main thread; everything after this touches Unity or Firebase's own state.
                await UniTask.SwitchToMainThread();
                if (status != DependencyStatus.Available)
                {
                    Debug.LogWarning($"Firebase is not available on this device: {status}.");
                    return false;
                }

                // Throws when the project has no google-services configuration (a missing google-services.json).
                _ = FirebaseApp.DefaultInstance;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Firebase failed to start: {exception.Message}");
                return false;
            }
        }
    }
}
