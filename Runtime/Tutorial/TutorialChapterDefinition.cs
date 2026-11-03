using System;
using System.Collections.Generic;
using Foundation.Requirements;

namespace Foundation.Tutorial
{
    public sealed class TutorialChapterDefinition
    {
        public TutorialChapterId Id { get; }
        public TutorialTriggerId Trigger { get; }
        public IReadOnlyList<TutorialStepId> Steps { get; }
        public IReadOnlyList<TutorialStepId> Checkpoints { get; }
        public int Priority { get; }
        public IRequirement Requirement { get; }

        public TutorialChapterDefinition(
            TutorialChapterId id,
            TutorialTriggerId trigger,
            IReadOnlyList<TutorialStepId> steps,
            IReadOnlyList<TutorialStepId> checkpoints = null,
            int priority = 0,
            IRequirement requirement = null)
        {
            if (steps == null)
                throw new ArgumentNullException(nameof(steps));
            if (steps.Count == 0)
                throw new ArgumentException($"Tutorial chapter '{id}' has no steps.", nameof(steps));

            Id = id;
            Trigger = trigger;
            Steps = steps;
            Checkpoints = checkpoints ?? Array.Empty<TutorialStepId>();
            Priority = priority;
            Requirement = requirement;
        }

        public bool IsCheckpoint(TutorialStepId step)
        {
            for (var index = 0; index < Checkpoints.Count; index++)
            {
                if (Checkpoints[index] == step)
                    return true;
            }

            return false;
        }

        public RequirementResult Evaluate() => Requirement?.Evaluate() ?? RequirementResult.Satisfied;
    }
}
