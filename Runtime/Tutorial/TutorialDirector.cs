using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Logging;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial
{
    public sealed class TutorialDirector : ITutorialTriggerSink, IDisposable
    {
        private const string LogCategory = "Tutorial";
        private const int MaxConsecutiveFailuresPerChapter = 3;

        private readonly TutorialChapterCatalog _catalog;
        private readonly TutorialService _service;
        private readonly TutorialProgressState _state;
        private readonly IReadOnlyList<ITutorialTrigger> _triggers;
        private readonly IReadOnlyList<ITutorialPresentationGate> _presentationGates;
        private readonly IAppLogger _logger;
        private readonly Dictionary<TutorialChapterId, int> _consecutiveFailures = new();
        private CancellationToken _cancellationToken = CancellationToken.None;
        private bool _activated;
        private bool _disposed;

        public IReadOnlyList<TutorialChapterId> Pending
        {
            get
            {
                var pending = new List<TutorialChapterDefinition>();
                var chapters = _catalog.Chapters;
                for (var index = 0; index < chapters.Count; index++)
                {
                    var chapter = chapters[index];
                    if (chapter.Id == _service.RunningChapter)
                        continue;
                    if (_state.GetOrCreate(chapter.Id).IsCandidate)
                        pending.Add(chapter);
                }

                pending.Sort(TutorialChapterCatalog.Compare);
                var ids = new TutorialChapterId[pending.Count];
                for (var index = 0; index < pending.Count; index++)
                    ids[index] = pending[index].Id;
                return ids;
            }
        }

        public TutorialChapterId? ActiveChapter => _service.RunningChapter;

        public TutorialDirector(
            TutorialChapterCatalog catalog,
            TutorialService service,
            TutorialProgressState state,
            IEnumerable<ITutorialTrigger> triggers = null,
            IEnumerable<ITutorialPresentationGate> presentationGates = null,
            IAppLogger logger = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _triggers = triggers == null
                ? Array.Empty<ITutorialTrigger>()
                : new List<ITutorialTrigger>(triggers);
            _presentationGates = presentationGates == null
                ? Array.Empty<ITutorialPresentationGate>()
                : new List<ITutorialPresentationGate>(presentationGates);
            _logger = logger;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var trigger in _triggers)
                trigger.Dispose();
        }

        public async UniTask InitializeAsync(CancellationToken cancellationToken = default)
        {
            _cancellationToken = cancellationToken;
            if (!_catalog.IsInitialized)
                await _catalog.InitializeAsync(cancellationToken);

            if (_activated)
                return;

            _activated = true;
            foreach (var trigger in _triggers)
                trigger.Activate(this);
        }

        public UniTask ResumeAsync(CancellationToken cancellationToken = default)
            => TryStartNextAsync(cancellationToken);

        public void Raise(TutorialTriggerId triggerId)
        {
            RaiseAsync(triggerId).Forget();
        }

        public async UniTask RaiseAsync(TutorialTriggerId triggerId, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                return;

            var chapters = _catalog.Chapters;
            foreach (var chapter in chapters)
            {
                if (chapter.Trigger != triggerId)
                    continue;

                TryQueue(chapter);
            }

            // A trigger means re-evaluate, so the attempt runs even when nothing new was queued: a candidate
            // that was blocked by a requirement or another chapter may be able to start now.
            await TryStartNextAsync(cancellationToken);
        }

        /// <summary>
        /// Raises a trigger and returns only when no chapter is running: those the trigger started, and any
        /// already running or queued next, have finished. For callers that must let the tutorial speak before
        /// the screen moves on.
        /// </summary>
        public async UniTask RaiseAndWaitAsync(TutorialTriggerId triggerId, CancellationToken cancellationToken = default)
        {
            await RaiseAsync(triggerId, cancellationToken);
            await WaitForIdleAsync(cancellationToken);
        }

        /// <summary>
        /// Returns once no chapter is running. For a caller that raised nothing but must not move on while an
        /// event-triggered chapter is speaking.
        /// </summary>
        public async UniTask WaitForIdleAsync(CancellationToken cancellationToken = default)
        {
            // A trigger returns immediately when another chapter owns the start loop; the yield lets a
            // chapter that just became eligible mark itself running before the idle check.
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            await UniTask.WaitWhile(() => _service.IsRunning, cancellationToken: cancellationToken);
        }

        public async UniTask TryStartNextAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || _service.IsRunning)
                return;

            var token = cancellationToken == CancellationToken.None ? _cancellationToken : cancellationToken;
            // Caps same-session retries of a chapter that keeps throwing. A caught exception does not
            // complete the chapter, so it stays a candidate and SelectNextEligible would hand it straight
            // back, which spams LogError in a tight loop when the cause is not transient.
            while (!_disposed)
            {
                // Checked on every iteration, not only on entry: a chapter queued by another trigger's Raise
                // while this loop is mid-chapter must not start the moment the loop is free, bypassing
                // whatever gated that trigger. Left queued (IsPending/IsCandidate untouched), it is picked up
                // by the gate owner's own Raise through RaiseAsync -> TryStartNextAsync.
                if (IsPresentationSuppressed())
                {
                    Log("chapter start deferred - a presentation gate is suppressed.");
                    return;
                }

                var chapter = SelectNextEligible();
                if (chapter == null)
                    return;

                _state.GetOrCreate(chapter.Id).IsPending = false;
                Log($"chapter '{chapter.Id}' started.");
                try
                {
                    await _service.RunAsync(chapter, token);
                    _consecutiveFailures.Remove(chapter.Id);
                    Log($"chapter '{chapter.Id}' completed.");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // TryStartNextAsync is reached from Raise(...).Forget(), so an exception here would
                    // vanish (UniTask logs it via Debug.LogException, which the dev console does not show).
                    // The chapter was then never marked completed (RunAsync's finally clears only
                    // RunningChapter), stayed started forever and the panel never showed.
                    LogError($"chapter '{chapter.Id}' failed and was aborted: {exception}");
                    var failures = _consecutiveFailures.GetValueOrDefault(chapter.Id, 0) + 1;
                    _consecutiveFailures[chapter.Id] = failures;
                    if (failures >= MaxConsecutiveFailuresPerChapter)
                        LogError($"chapter '{chapter.Id}' gave up after {failures} consecutive failures this session.");
                }
            }
        }

        private bool IsPresentationSuppressed()
        {
            foreach (var gate in _presentationGates)
            {
                if (gate.IsSuppressed)
                    return true;
            }

            return false;
        }

        private bool TryQueue(TutorialChapterDefinition chapter)
        {
            if (_state.IsCompleted(chapter.Id))
            {
                Log($"chapter '{chapter.Id}' ignored its trigger: already completed.");
                return false;
            }

            if (_service.RunningChapter == chapter.Id)
                return false;

            var progress = _state.GetOrCreate(chapter.Id);
            if (progress.IsCandidate)
                return false;

            progress.IsPending = true;
            Log($"chapter '{chapter.Id}' queued.");
            return true;
        }

        private TutorialChapterDefinition SelectNextEligible()
        {
            TutorialChapterDefinition best = null;
            var chapters = _catalog.Chapters;
            foreach (var chapter in chapters)
            {
                if (!_state.GetOrCreate(chapter.Id).IsCandidate
                    || (_consecutiveFailures.TryGetValue(chapter.Id, out var failures) && failures >= MaxConsecutiveFailuresPerChapter))
                    continue;

                var evaluation = chapter.Evaluate();
                if (!evaluation.IsSatisfied)
                {
                    Log($"chapter '{chapter.Id}' skipped: requirement '{evaluation.FailureCode}' not met.");
                    continue;
                }

                if (best == null || TutorialChapterCatalog.Compare(chapter, best) < 0)
                    best = chapter;
            }

            return best;
        }

        private void Log(string message) => _logger?.Log(LogLevel.Debug, LogCategory, message);
        private void LogError(string message) => _logger?.Log(LogLevel.Error, LogCategory, message);
    }
}
