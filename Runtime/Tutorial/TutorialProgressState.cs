using System;
using System.Collections.Generic;

namespace Foundation.Tutorial
{
    public enum TutorialChapterStatus
    {
        NotStarted,
        InProgress,
        Completed
    }

    public sealed class TutorialChapterProgress
    {
        private readonly HashSet<TutorialStepId> _completedSteps = new();

        public TutorialChapterId ChapterId { get; }

        public TutorialChapterStatus Status { get; internal set; } = TutorialChapterStatus.NotStarted;

        public bool IsPending { get; internal set; }

        public TutorialStepId? CurrentStep { get; internal set; }

        public IReadOnlyCollection<TutorialStepId> CompletedSteps => _completedSteps;

        public bool IsCompleted => Status == TutorialChapterStatus.Completed;

        public bool IsCandidate => Status != TutorialChapterStatus.Completed
            && (IsPending || Status == TutorialChapterStatus.InProgress);

        public TutorialChapterProgress(TutorialChapterId chapterId)
        {
            ChapterId = chapterId;
        }

        public bool HasCompleted(TutorialStepId step) => _completedSteps.Contains(step);

        internal void CompleteStep(TutorialStepId step) => _completedSteps.Add(step);

        internal void Set(
            TutorialChapterStatus status,
            TutorialStepId? currentStep,
            IEnumerable<TutorialStepId> completedSteps,
            bool isPending = false)
        {
            Status = status;
            CurrentStep = currentStep;
            IsPending = isPending;
            _completedSteps.Clear();
            foreach (var step in completedSteps ?? Array.Empty<TutorialStepId>())
                _completedSteps.Add(step);
        }

        internal TutorialChapterProgress Clone()
        {
            var clone = new TutorialChapterProgress(ChapterId);
            clone.Set(Status, CurrentStep, _completedSteps, IsPending);
            return clone;
        }
    }

    public sealed class TutorialProgressState
    {
        private readonly Dictionary<TutorialChapterId, TutorialChapterProgress> _chapters = new();

        public IReadOnlyDictionary<TutorialChapterId, TutorialChapterProgress> Chapters => _chapters;

        public TutorialChapterProgress GetOrCreate(TutorialChapterId chapterId)
        {
            if (_chapters.TryGetValue(chapterId, out var progress))
                return progress;

            progress = new TutorialChapterProgress(chapterId);
            _chapters.Add(chapterId, progress);
            return progress;
        }

        public bool IsCompleted(TutorialChapterId chapterId)
            => _chapters.TryGetValue(chapterId, out var progress) && progress.IsCompleted;

        public TutorialChapterStatus GetStatus(TutorialChapterId chapterId)
            => _chapters.TryGetValue(chapterId, out var progress)
                ? progress.Status
                : TutorialChapterStatus.NotStarted;

        internal void Restore(IEnumerable<TutorialChapterProgress> chapters)
        {
            _chapters.Clear();
            foreach (var chapter in chapters ?? Array.Empty<TutorialChapterProgress>())
            {
                if (chapter == null)
                    throw new InvalidOperationException("Tutorial save contains an empty chapter entry.");
                if (!_chapters.TryAdd(chapter.ChapterId, chapter))
                    throw new InvalidOperationException($"Tutorial save contains chapter '{chapter.ChapterId}' more than once.");
            }
        }
    }
}
