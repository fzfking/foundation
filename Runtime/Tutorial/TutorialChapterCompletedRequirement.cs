using System;
using System.Collections.Generic;
using Foundation.Requirements;

namespace Foundation.Tutorial
{
    public sealed class TutorialChapterCompletedRequirement : IRequirement
    {
        public const string FailureCode = "tutorial.chapter-not-completed";
        public const string ChapterIdArgument = "chapterId";

        private readonly TutorialProgressState _state;
        private readonly TutorialChapterId _chapterId;
        private readonly RequirementResult _failure;

        public TutorialChapterCompletedRequirement(TutorialProgressState state, TutorialChapterId chapterId)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _chapterId = chapterId;
            _failure = RequirementResult.Failed(
                FailureCode,
                new Dictionary<string, object> { [ChapterIdArgument] = chapterId.Value });
        }

        public RequirementResult Evaluate()
            => _state.IsCompleted(_chapterId) ? RequirementResult.Satisfied : _failure;
    }
}
