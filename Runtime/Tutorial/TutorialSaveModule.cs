using System;
using System.Collections.Generic;
using Foundation.Save;

namespace Foundation.Tutorial
{
    public sealed class TutorialSaveModule : FeatureSaveModule<TutorialSnapshot>
    {
        private readonly TutorialProgressState _state;
        private readonly TutorialChapterCatalog _catalog;
        private readonly TutorialService _service;

        public override string SegmentId => "tutorial";

        public TutorialSaveModule(
            TutorialProgressState state,
            TutorialChapterCatalog catalog,
            TutorialService service)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _service = service ?? throw new ArgumentNullException(nameof(service));
        }

        public override TutorialSnapshot Capture()
        {
            var chapters = new List<TutorialChapterSnapshot>();
            foreach (var pair in _state.Chapters)
            {
                var progress = _catalog.TryGet(pair.Key, out var chapter)
                    ? _service.CaptureCheckpoint(chapter)
                    : pair.Value;
                chapters.Add(ToSnapshot(progress));
            }

            return new TutorialSnapshot { Chapters = chapters.ToArray() };
        }

        public override void Restore(TutorialSnapshot snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            var chapters = new List<TutorialChapterProgress>();
            foreach (var chapterSnapshot in snapshot.Chapters ?? Array.Empty<TutorialChapterSnapshot>())
            {
                if (chapterSnapshot == null || string.IsNullOrWhiteSpace(chapterSnapshot.ChapterId))
                    throw new InvalidOperationException("Tutorial save contains a chapter without an id.");

                var progress = new TutorialChapterProgress(new TutorialChapterId(chapterSnapshot.ChapterId));
                var completed = new List<TutorialStepId>();
                foreach (var stepId in chapterSnapshot.CompletedStepIds ?? Array.Empty<string>())
                    completed.Add(new TutorialStepId(stepId));

                progress.Set(
                    ToStatus(chapterSnapshot.Status),
                    string.IsNullOrWhiteSpace(chapterSnapshot.CurrentStepId)
                        ? null
                        : new TutorialStepId(chapterSnapshot.CurrentStepId),
                    completed,
                    chapterSnapshot.IsPending);
                chapters.Add(progress);
            }

            _state.Restore(chapters);
        }

        private static TutorialChapterSnapshot ToSnapshot(TutorialChapterProgress progress)
        {
            var completed = new List<string>();
            foreach (var step in progress.CompletedSteps)
                completed.Add(step.Value);

            return new TutorialChapterSnapshot
            {
                ChapterId = progress.ChapterId.Value,
                Status = (int)progress.Status,
                CurrentStepId = progress.CurrentStep?.Value,
                IsPending = progress.IsPending,
                CompletedStepIds = completed.ToArray()
            };
        }

        private static TutorialChapterStatus ToStatus(int value)
            => Enum.IsDefined(typeof(TutorialChapterStatus), value)
                ? (TutorialChapterStatus)value
                : throw new InvalidOperationException($"Tutorial save contains unknown chapter status '{value}'.");
    }
}
