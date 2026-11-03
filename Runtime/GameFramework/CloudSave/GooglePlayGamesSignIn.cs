using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Foundation.Logging;
using Cysharp.Threading.Tasks;
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using UnityEngine;

using UnityDebug = UnityEngine.Debug;

namespace Foundation.GameFramework.CloudSave
{
    /// <summary>
    /// The Play Games sign-in of one app session: silent first, then the interactive prompt (which may show
    /// system UI), the player's cloud-save toggle, and a record of every attempt. The platform is a process
    /// singleton and sign-in starts in the bootstrap scene, before any container exists, so the game creates
    /// one instance there and hands the same instance to the container.
    /// </summary>
    public sealed class GooglePlayGamesSignIn
    {
        /// <summary>The wait the save load gives the silent sign-in before it falls back to the prompt.</summary>
        public static readonly TimeSpan DefaultAuthenticationTimeout = TimeSpan.FromSeconds(10);

        private const string LogCategory = "GooglePlayGames";
        private const int HistoryLimit = 8;

        private readonly ICloudSavePreference _preference;
        private readonly TimeSpan _authenticationTimeout;
        private readonly IAppLogger _logger;
        private readonly List<GooglePlayGamesAuthReport> _history = new(HistoryLimit);
        private UniTask<bool> _silentAuthentication = UniTask.FromResult(false);
        private bool _silentStarted;

        // AwaitAsync runs twice a session (the configuration load, then the save load), so the interactive step
        // must be idempotent too: a player who dismissed the prompt the first time must not see it again.
        private bool _interactiveAttempted;
        private bool _interactiveResult;

        /// <summary>
        /// Raised after every sign-in attempt with what the SDK answered and how the device looked, so a
        /// failure on a player's phone can be told apart afterwards. Main thread; the report is also logged.
        /// </summary>
        public event Action<GooglePlayGamesAuthReport> AuthenticationReported;

        /// <summary>The reports of this session, oldest first, for a listener that attached after sign-in began.</summary>
        public IReadOnlyList<GooglePlayGamesAuthReport> AuthenticationHistory => _history;

        public bool IsPlatformAvailable
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Whether the player's toggle allows cloud saving, independent of being signed in.</summary>
        public bool IsPreferenceEnabled => _preference.IsEnabled;

        /// <summary>Whether cloud saving is active: Play Games is supported, the toggle is on, and sign-in succeeded.</summary>
        public bool IsActive
#if UNITY_ANDROID
            => IsPlatformAvailable && _preference.IsEnabled && PlayGamesPlatform.Instance.IsAuthenticated();
#else
            => false;
#endif

        public GooglePlayGamesSignIn(
            ICloudSavePreference preference,
            TimeSpan authenticationTimeout,
            IAppLogger logger = null)
        {
            if (authenticationTimeout <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(authenticationTimeout));

            _preference = preference ?? throw new ArgumentNullException(nameof(preference));
            _authenticationTimeout = authenticationTimeout;
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
        }

        /// <summary>
        /// Starts the silent sign-in as early as possible (bootstrap scene, before Addressables), overlapping
        /// content loading instead of delaying the first save load. Safe to call repeatedly.
        /// </summary>
        public void WarmUp() => StartSilent();

        /// <summary>
        /// Persists the settings-toggle choice. Not a sign-out: the account connection is untouched, only this
        /// game's cloud saving is switched off.
        /// </summary>
        public void SetEnabled(bool enabled) => _preference.SetEnabled(enabled);

        /// <summary>
        /// Turns cloud saving on (persisted immediately) and, when signed out, runs the interactive sign-in.
        /// Returns whether cloud saving is active afterwards. If sign-in fails the toggle stays on and the next
        /// launch's silent sign-in activates it.
        /// </summary>
        public UniTask<bool> TryEnableAsync(CancellationToken cancellationToken)
            => EnableAsync(GooglePlayGamesAuthStage.Manual, cancellationToken);

        /// <summary>
        /// Resolves sign-in: silent first, then the prompt when no credential is cached. Settles before any
        /// other Addressables activity starts, so native UI never competes with content loading.
        /// </summary>
        public async UniTask<bool> AwaitAsync(CancellationToken cancellationToken)
        {
#if UNITY_ANDROID
            if (!IsPlatformAvailable)
                return false;

            if (PlayGamesPlatform.Instance.IsAuthenticated())
                return true;

            // Offline devices fail sign-in on their own quickly; without this shortcut a cold offline launch
            // could stall the whole save load until the timeout elapses.
            if (Application.internetReachability == NetworkReachability.NotReachable)
            {
                Report(GooglePlayGamesAuthStage.SkippedOffline, "Offline", null, null);
                return false;
            }

            StartSilent();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_authenticationTimeout);
            var silentlyAuthenticated = false;
            var waitTimer = Stopwatch.StartNew();
            try
            {
                silentlyAuthenticated = await _silentAuthentication.AttachExternalCancellation(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Sign-in did not settle within the timeout - fall through to the prompt rather than giving up.
                Report(GooglePlayGamesAuthStage.SilentTimeout, "Timeout", waitTimer, null);
            }

            if (silentlyAuthenticated)
                return true;

            // Silent sign-in needs a cached Play Games credential and never shows UI. None means a fresh install
            // or wiped app data. The toggle is on, so the player wants their progress restored: ask them to sign
            // in instead of starting a new empire over an existing cloud save.
            if (!_preference.IsEnabled)
                return false;

            // Shown once per app session however many callers ask: a declined prompt must not reappear seconds later.
            if (_interactiveAttempted)
                return _interactiveResult;

            _interactiveAttempted = true;
            _interactiveResult = await EnableAsync(GooglePlayGamesAuthStage.Interactive, cancellationToken);
            return _interactiveResult;
#else
            return false;
#endif
        }

        private async UniTask<bool> EnableAsync(GooglePlayGamesAuthStage stage, CancellationToken cancellationToken)
        {
#if UNITY_ANDROID
            if (!IsPlatformAvailable)
                return false;

            _preference.SetEnabled(true);
            if (PlayGamesPlatform.Instance.IsAuthenticated())
                return true;

            cancellationToken.ThrowIfCancellationRequested();
            var completion = new UniTaskCompletionSource<bool>();
            var timer = Stopwatch.StartNew();
            try
            {
                PlayGamesPlatform.Instance.ManuallyAuthenticate(status =>
                {
                    Report(stage, status.ToString(), timer, null);
                    completion.TrySetResult(status == SignInStatus.Success);
                });
            }
            catch (Exception exception)
            {
                Report(stage, "Exception", timer, exception.Message);
                throw;
            }

            return await completion.Task.AttachExternalCancellation(cancellationToken);
#else
            return false;
#endif
        }

        private void StartSilent()
        {
            if (_silentStarted || !IsPlatformAvailable)
                return;

            _silentStarted = true;
            // Preserved: AwaitAsync awaits this from more than one call per session, and a plain UniTask throws
            // "Token version is not matched" on the second await.
            _silentAuthentication = AuthenticateSilentlyAsync().Preserve();
        }

        private async UniTask<bool> AuthenticateSilentlyAsync()
        {
#if UNITY_ANDROID
            var timer = Stopwatch.StartNew();
            try
            {
                PlayGamesPlatform.DebugLogEnabled = UnityDebug.isDebugBuild;
                var platform = PlayGamesPlatform.Activate();
                var completion = new UniTaskCompletionSource<bool>();
                platform.Authenticate(status =>
                {
                    Report(GooglePlayGamesAuthStage.Silent, status.ToString(), timer, null);
                    completion.TrySetResult(status == SignInStatus.Success);
                });
                return await completion.Task;
            }
            catch (Exception exception)
            {
                Report(GooglePlayGamesAuthStage.Silent, "Exception", timer, exception.ToString());
                return false;
            }
#else
            return false;
#endif
        }

        private void Report(GooglePlayGamesAuthStage stage, string status, Stopwatch timer, string detail)
        {
            var report = new GooglePlayGamesAuthReport(
                stage, status, timer?.ElapsedMilliseconds ?? 0L, detail, GooglePlayGamesDiagnostics.Collect());
            foreach (var warning in report.Diagnostics.Warnings)
                _logger.Log(LogLevel.Warning, LogCategory, warning);
            if (_history.Count == HistoryLimit)
                _history.RemoveAt(0);
            _history.Add(report);
            if (report.Succeeded)
                _logger.Log(LogLevel.Info, LogCategory, $"Sign-in: {report}");
            else
                _logger.Log(LogLevel.Warning, LogCategory, $"Sign-in did not succeed: {report}");

            try
            {
                AuthenticationReported?.Invoke(report);
            }
            catch (Exception exception)
            {
                // A listener must never break sign-in, and sign-in must never break a save load.
                _logger.Log(LogLevel.Warning, LogCategory, $"A sign-in report listener threw: {exception.Message}");
            }
        }
    }
}
