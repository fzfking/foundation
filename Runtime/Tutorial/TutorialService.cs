using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial
{
    public sealed class TutorialService
    {
        private readonly TutorialRegistry _registry;
        private readonly TutorialProgressState _state;

        public bool IsRunning => RunningChapter.HasValue;

        public TutorialChapterId? RunningChapter { get; private set; }

        public TutorialStepId? CurrentStep { get; private set; }

        public TutorialService(TutorialRegistry registry, TutorialProgressState state)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _state = state ?? throw new ArgumentNullException(nameof(state));
        }

        public async UniTask RunAsync(
            TutorialChapterDefinition chapter,
            CancellationToken cancellationToken = default)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));
            if (IsRunning)
                throw new InvalidOperationException(
                    $"Tutorial chapter '{RunningChapter}' is already running; chapters never run in parallel.");

            var progress = _state.GetOrCreate(chapter.Id);
            if (progress.IsCompleted)
                return;

            RunningChapter = chapter.Id;
            progress.Status = TutorialChapterStatus.InProgress;
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var next = FindNextStep(chapter, progress);
                    if (!next.HasValue)
                        break;

                    progress.CurrentStep = next.Value;
                    CurrentStep = next.Value;
                    await _registry.Resolve(next.Value).ExecuteAsync(cancellationToken);
                    progress.CompleteStep(next.Value);
                }

                progress.Status = TutorialChapterStatus.Completed;
                progress.CurrentStep = null;
                progress.IsPending = false;
            }
            finally
            {
                RunningChapter = null;
                CurrentStep = null;
            }
        }

        public TutorialChapterProgress CaptureCheckpoint(TutorialChapterDefinition chapter)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));

            var progress = _state.GetOrCreate(chapter.Id);
            var captured = new TutorialChapterProgress(chapter.Id);
            if (progress.Status != TutorialChapterStatus.InProgress)
            {
                captured.Set(progress.Status, progress.CurrentStep, progress.CompletedSteps, progress.IsPending);
                return captured;
            }

            var lastCheckpointIndex = -1;
            for (var index = 0; index < chapter.Steps.Count; index++)
            {
                var step = chapter.Steps[index];
                if (chapter.IsCheckpoint(step) && progress.HasCompleted(step))
                    lastCheckpointIndex = index;
            }

            if (lastCheckpointIndex < 0)
            {
                // Rolled all the way back, but the chapter was running: it stays a candidate so
                // the next session resumes it instead of waiting for its trigger again.
                captured.Set(TutorialChapterStatus.NotStarted, null, Array.Empty<TutorialStepId>(), true);
                return captured;
            }

            var completed = new List<TutorialStepId>();
            for (var index = 0; index <= lastCheckpointIndex; index++)
            {
                var step = chapter.Steps[index];
                if (progress.HasCompleted(step))
                    completed.Add(step);
            }

            captured.Set(TutorialChapterStatus.InProgress, FindFirstIncomplete(chapter, completed), completed);
            return captured;
        }

        private static TutorialStepId? FindNextStep(
            TutorialChapterDefinition chapter,
            TutorialChapterProgress progress)
        {
            for (var index = 0; index < chapter.Steps.Count; index++)
            {
                if (!progress.HasCompleted(chapter.Steps[index]))
                    return chapter.Steps[index];
            }

            return null;
        }

        private static TutorialStepId? FindFirstIncomplete(
            TutorialChapterDefinition chapter,
            IReadOnlyList<TutorialStepId> completed)
        {
            foreach (var step in chapter.Steps)
            {
                var isCompleted = false;
                foreach (var tutorialStepId in completed)
                {
                    if (tutorialStepId != step)
                        continue;

                    isCompleted = true;
                    break;
                }

                if (!isCompleted)
                    return step;
            }

            return null;
        }
    }
}
